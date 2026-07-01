namespace ContextMessenger.Data;

public sealed class ReadOnlySqlException : Exception
{
    public ReadOnlySqlException(string message)
        : base(message)
    {
    }
}
