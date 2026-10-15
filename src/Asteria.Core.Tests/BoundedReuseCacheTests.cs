using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BoundedReuseCacheTests
{
    [Fact]
    public void RestoredResourcesAreRemovedFromCacheAndNotDisposed()
    {
        var cache = new BoundedReuseCache<int, object>(2);
        var resource = new object();
        var destroyed = new List<object>();

        cache.Store(4, resource, destroyed.Add);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryTake(4, out var restored));
        Assert.Same(resource, restored);
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryTake(4, out _));
        Assert.Empty(destroyed);
    }

    [Fact]
    public void OverflowDisposesOldestResourcesInDeterministicOrder()
    {
        var cache = new BoundedReuseCache<int, object>(2);
        var a = new object();
        var b = new object();
        var c = new object();
        var destroyed = new List<object>();

        cache.Store(1, a, destroyed.Add);
        cache.Store(2, b, destroyed.Add);
        cache.Store(3, c, destroyed.Add);
        Assert.Single(destroyed);
        Assert.Same(a, destroyed[0]);
        Assert.False(cache.TryTake(1, out _));
        Assert.Equal(2, cache.Count);
        cache.Clear(destroyed.Add);
        Assert.Equal(new[] { a, b, c }, destroyed);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void RepeatedOwnershipAndInvalidCapacityAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundedReuseCache<string, object>(0));

        var cache = new BoundedReuseCache<string, object>(1);
        cache.Store("chunk", new object(), _ => { });
        Assert.Throws<InvalidOperationException>(() =>
            cache.Store("chunk", new object(), _ => { }));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void TakeAndReinsertMakesResourceNewest()
    {
        var cache = new BoundedReuseCache<int, object>(2);
        var first = new object();
        var second = new object();
        var latest = new object();
        var disposed = new List<object>();

        cache.Store(1, first, disposed.Add);
        cache.Store(2, second, disposed.Add);
        Assert.True(cache.TryTake(1, out var taken));
        cache.Store(1, taken!, disposed.Add);
        cache.Store(3, latest, disposed.Add);

        Assert.Single(disposed);
        Assert.Same(second, disposed[0]);
        Assert.True(cache.TryTake(1, out var restored));
        Assert.Same(first, restored);
        Assert.True(cache.TryTake(3, out _));
    }
}
