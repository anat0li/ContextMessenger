namespace ContextMessenger.Protocol.Dispatch;

public interface IRequestIdCache
{
    bool TryAdd(string id);
    bool Contains(string id);
    void Clear();
    int Count { get; }
}
