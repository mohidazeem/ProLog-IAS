# ProLog-IAS — Inventory Aggregation Service

A .NET 10 web service that reads every record from the paginated ProLog Inventory API, validates that the collected dataset is complete and consistent, aggregates it by category and serves the result from one GET endpoint.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (LTS). `global.json` pins SDK 10.0.x with feature roll-forward.

## Configuration

| Key | Where | Notes |
|---|---|---|
| `InventoryApi:BaseAddress` | user secrets / env var | Required. Must be an absolute **HTTPS** URL. |
| `InventoryApi:ApiKey` | user secrets / env var | Required. Never commit it. |
| `InventoryApi:ApiKeyHeaderName` | `appsettings.json` | Header that carries the key. Defaults to `X-Api-Key`. Change it if the assignment email names a different header. |
| `InventoryApi:Resilience` | `appsettings.json` | Timeouts and retry policy for upstream calls (see below). |
| `InventoryCollection:PageSize` | `appsettings.json` | 1–25, default 25 (fewest round trips). |
| `InventoryCollection:MaxPageAttempts` | `appsettings.json` | How many times one page is requested before it is rejected as inconsistent. |

All options are validated at startup, so missing or invalid configuration stops the app with a clear message before it accepts any traffic.

### Setting the API key (user secrets, for local development)

```bash
cd src/InventoryAggregation.Api
dotnet user-secrets set "InventoryApi:BaseAddress" "https://<base-url-from-email>"
dotnet user-secrets set "InventoryApi:ApiKey" "<api-key-from-email>"
```

User secrets are stored outside the repository (`%APPDATA%\Microsoft\UserSecrets` on Windows, `~/.microsoft/usersecrets` on Linux/macOS). They are loaded only in the `Development` environment, which is what `dotnet run` uses through `launchSettings.json`.

In any other environment, use environment variables instead:

```bash
export InventoryApi__BaseAddress="https://<base-url>"
export InventoryApi__ApiKey="<api-key>"
```

## Running

```bash
dotnet run --project src/InventoryAggregation.Api
```

Then call:

```bash
curl http://localhost:5220/api/inventory/categories
```

## Running the tests

```bash
dotnet test
```

The tests do not call the real API and need no secrets.

## Endpoint

`GET /api/inventory/categories`

| Status | When |
|---|---|
| `200 OK` | Report produced from the complete dataset. |
| `502 Bad Gateway` | The upstream API could not be reached, kept failing, or kept returning inconsistent data. Body is `application/problem+json`. |
| `500 Internal Server Error` | Unexpected failure. Body is `application/problem+json` without internal details. |

Response shape (follows the suggested shape):

```json
{
  "categories": [
    {
      "category": "Electronics",
      "totalInventoryValue": 1799.88,
      "availableRecordCount": 1,
      "mostRecentlyUpdated": { "id": 1001, "name": "Thermal Receipt Printer", "category": "Electronics", "price": 149.99, "quantity": 12, "lastUpdated": "2026-10-15T12:30:00+00:00" },
      "items": [
        { "id": 1001, "name": "Thermal Receipt Printer", "category": "Electronics", "price": 149.99, "quantity": 12, "lastUpdated": "2026-10-15T12:30:00+00:00" }
      ]
    }
  ]
}
```

Deliberate deviations: `mostRecentlyUpdated` and `items` carry the full record, including `category`, because the domain record is serialized directly rather than mapped to a separate response DTO. Timestamps are serialized as `+00:00` rather than `Z`; both are valid ISO 8601 UTC.

## Architecture

```
src/InventoryAggregation.Core   domain: models, page validation/collection, aggregation. No HTTP or ASP.NET references.
src/InventoryAggregation.Api    host: typed HttpClient, resilience, configuration, exception mapping, endpoint.
tests/InventoryAggregation.Tests
```

- **`IInventoryPageSource`** (Core) is the only thing the domain knows about where data comes from: "give me page N". This is the seam that keeps HTTP out of the business logic. The project boundary enforces it, because Core cannot reference ASP.NET or HttpClient types.
- **`InventoryApiClient`** (Api) implements that port as a typed client created through `IHttpClientFactory`. It turns every failure into one of two domain exceptions: `InventoryUnavailableException` (could not get an answer) or `InventoryDataException` (got an answer that cannot be read).
- **Resilience** comes from `Microsoft.Extensions.Http.Resilience` (Polly v8), added with `AddStandardResilienceHandler`. Per HTTP call it provides:
  - a total timeout
  - retries with exponential backoff and jitter on 5xx, 408, 429 and transport errors, honouring `Retry-After`
  - a per-attempt timeout
  - a circuit breaker, so a dead upstream is not hammered

  4xx responses other than 408 and 429 are not retried, because repeating them cannot help.
- **`InventoryCollector`** (Core) handles a second kind of failure: a `200` with wrong content. It fetches page 1, treats its totals as the reference, then fetches pages 2..N and one page past the end (N+1). A page is accepted only if all of these hold:
  - its `page` and `pageSize` match the request
  - `totalPages` is consistent with `totalRecords`
  - its totals match page 1
  - every record has a category, a non-negative price and a non-negative quantity
  - a record whose id was already seen is an exact copy (it is then dropped); the same id with different content rejects the page
  - it contributes exactly the expected number of *new* records: full pages, then the remainder, then zero on page N+1

  A rejected page is requested again up to `MaxPageAttempts` times. If it still fails, the whole request fails rather than returning a partial report. Per-page new-record counts, unique ids and an empty page N+1 together guarantee that exactly `totalRecords` distinct records were collected and none exist beyond them.
- **`InventoryAggregator`** (Core) is a pure transformation, `records -> report`, with no I/O, so it is tested with plain objects.
- **`InventoryReportService`** orchestrates collect, then aggregate, behind `IInventoryReportService`, the endpoint's only dependency.
- **`InventoryExceptionHandler`** maps the domain exceptions to `502` ProblemDetails. Everything else falls through to the framework's ProblemDetails `500`. No exception reaches the client unhandled.

**Lifetimes:** the typed client and the collector are transient, the report service is scoped and the stateless aggregator is a singleton. Nothing long-lived captures an `HttpClient`, so `IHttpClientFactory` can rotate its handlers (DNS changes, socket reuse). Responses are disposed and deserialized straight from the stream (`ResponseHeadersRead` + `ReadFromJsonAsync`), so no whole response body is buffered as a string. Cancellation from the caller flows through every async call.

## Observed upstream behaviour

Probing the real API (157 records, 7 pages at size 25) showed:

- **Overlapping pages.** Pages 2, 4 and 6 start with an exact copy of the previous page's last record, so they return 26 records. Each still contains 25 new records, so the copy is dropped, not treated as a missing or extra record.
- **Rate limiting.** 5 requests per 10 seconds. The sixth request gets `429` with a `Retry-After` header (5–6 s). The retry strategy honours `Retry-After`, so a full report takes about 13 s instead of failing.
- **Inconsistent category spelling.** For example `"Electronics"`, `" electronics "` and `"Electronics "`.
- **Pages past the end** return `200` with an empty `records` list, which the collector uses as its end-of-data check.
- **`pageSize` above 25** returns `400`.
- **21 of 157 records have quantity 0.**

The service's output for this dataset was cross-checked against an independent script over the raw pages; values and counts match.

## Decisions and assumptions

- **Zero quantity** records are excluded before grouping. A category whose records all have quantity 0 does not appear in the output.
- **Category grouping** trims whitespace and ignores case. The displayed name is the most common trimmed spelling within the category (ties broken by ordinal order), so `"Electronics"` wins over `"electronics"` and `"ELECTRONICS"`. `items` keep the category exactly as the upstream sent it.
- **Repeated records:** an id seen again with identical content is dropped. The same id with different content means the source contradicted itself, so the page is rejected and requested again.
- **Ties on `lastUpdated`** are broken by the lowest `id`, so the result is deterministic.
- **Ordering:** categories are sorted alphabetically and items by `id`.
- **Money** uses `decimal` end to end. `totalInventoryValue` is the exact sum of `price × quantity` and is not rounded.
- **Invalid records** (blank category, negative price or quantity, missing or null fields, wrong types) invalidate the page they came from rather than being silently skipped. Skipping them would produce a report that looks complete but is not.
- **Fail closed:** if any page cannot be retrieved consistently, the endpoint returns `502` instead of a partial result.
- **HTTPS only** for the upstream base address, so the API key is never sent in clear text.
- **Empty dataset:** `totalRecords = 0` with `totalPages` of 0 or 1 is accepted and produces an empty `categories` list.

## Known limitations and next steps

- **No caching.** The dataset is static, yet every request re-reads all 8 pages. With the upstream rate limit, each report takes about 13 s, and concurrent callers compete for the same 5-requests-per-10-seconds budget, so several simultaneous requests could exhaust their retries and get `502`. This is the first thing I would add: cache the report (for example `HybridCache`, which also collapses concurrent misses into one upstream walk) with an expiry.
- **Rate limiting is reactive.** The client discovers the limit through `429` + `Retry-After`. A client-side limiter (a shared sliding-window `RateLimiter` plugged into the resilience pipeline) would pace requests and avoid the 429s entirely.
- **Pages are fetched sequentially.** Given the rate limit, parallel fetching would not help.
- **Validation is page-local after page 1.** If page 1 itself carries wrong totals, every later page is rejected and the request fails. It does not return wrong data, but restarting the whole walk once would recover from a transient bad first page.
- **Strict page size.** If the API silently capped `pageSize` below the requested value, every page would be rejected. This is deliberate (fail loudly rather than mis-page), but the setting would need adjusting.
- **Retry budget.** HTTP retries (per call) multiply with collector re-requests (per page). Worst case for one page is `MaxPageAttempts × (1 + MaxRetryAttempts)` calls. The total timeout and circuit breaker bound this, but an overall deadline for the whole report would be cleaner.
- **The endpoint has no authentication** and no rate limiting. It was out of scope for the exercise; in production it would sit behind the platform's auth.
- **No OpenAPI or health checks.** Both are cheap additions if the service were deployed.
