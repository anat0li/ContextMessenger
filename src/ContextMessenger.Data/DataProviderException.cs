namespace ContextMessenger.Data;

public sealed class DataProviderException : Exception
{
    public DataProviderException(string message)
        : base(message)
    {
    }

    public DataProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
