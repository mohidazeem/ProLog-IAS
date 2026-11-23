namespace InventoryAggregation.Core;

public sealed class InventoryUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class InventoryDataException(string message, Exception? innerException = null)
    : Exception(message, innerException);
