namespace ContextMessenger.Protocol.Dispatch;

public sealed class InMemoryRequestIdCache : IRequestIdCache
{
    private readonly Lock _lock = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (_lock)
                return _ids.Count;
        }
    }

    public bool TryAdd(string id)
    {
        lock (_lock)
            return _ids.Add(id);
    }

    public bool Contains(string id)
    {
        lock (_lock)
            return _ids.Contains(id);
    }

    public void Clear()
    {
        lock (_lock)
            _ids.Clear();
    }
}
