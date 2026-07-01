namespace ContextMessenger.Data;

public sealed class DataConnectionException : Exception
{
    public DataConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
