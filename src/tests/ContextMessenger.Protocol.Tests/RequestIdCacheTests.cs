using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Tests;

public sealed class RequestIdCacheTests
{
    [Fact]
    public void TryAdd_returns_true_on_first_id()
    {
        var cache = new InMemoryRequestIdCache();
        Assert.True(cache.TryAdd("abc"));
        Assert.True(cache.Contains("abc"));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void TryAdd_returns_false_on_duplicate_id()
    {
        var cache = new InMemoryRequestIdCache();
        Assert.True(cache.TryAdd("abc"));
        Assert.False(cache.TryAdd("abc"));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Clear_resets_state()
    {
        var cache = new InMemoryRequestIdCache();
        cache.TryAdd("abc");
        cache.Clear();
        Assert.False(cache.Contains("abc"));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void TryAdd_is_safe_under_concurrent_callers()
    {
        var cache = new InMemoryRequestIdCache();
        var successes = 0;

        Parallel.For(0, 100, _ =>
        {
            if (cache.TryAdd("abc"))
                Interlocked.Increment(ref successes);
        });

        Assert.Equal(1, successes);
        Assert.Equal(1, cache.Count);
    }
}
