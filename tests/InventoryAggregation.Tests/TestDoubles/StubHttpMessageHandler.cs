namespace InventoryAggregation.Tests.TestDoubles;

/// <summary>Answers each request with <c>respond(callNumber, cancellationToken)</c> and records what was sent.</summary>
internal sealed class StubHttpMessageHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return respond(Requests.Count, cancellationToken);
    }
}
