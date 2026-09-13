namespace MediaEngine.Storage;

public sealed class DatabaseWriteAccessException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
