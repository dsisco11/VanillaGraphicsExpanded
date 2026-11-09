using System.Reactive.Linq;
using System.Reactive.Subjects;
using VanillaGraphicsExpanded.Rendering.Enablement;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache;

/// <summary>Checks cached Rx values, duplicate suppression and subscription lifetimes.</summary>
public sealed class CachedObservableTests
{
    #region Public API

    /// <summary>One upstream computation supplies cached reads and late subscribers without duplicate changes.</summary>
    [Fact]
    public void CachedValueSharesConnectionSuppressesDuplicatesAndDetaches()
    {
        // Arrange: one seeded source and two subscribers make shared connection behavior observable.
        using var source = new BehaviorSubject<int>(1);
        int evaluations = 0;
        using var cache = new CachedObservable<bool>(source.Select(value => { evaluations++; return value > 0; }));
        var first = new List<bool>();
        using var firstSubscription = cache.Subscribe(first.Add);
        // Act: read twice, emit duplicate projected values, then attach a late subscriber.
        Assert.True(cache.Value);
        Assert.True(cache.Value);
        source.OnNext(2);
        source.OnNext(0);
        var late = new List<bool>();
        using var lateSubscription = cache.Subscribe(late.Add);
        // Assert: duplicate projected values are suppressed and the cache disconnects on disposal.
        Assert.Equal(new[] { true, false }, first);
        Assert.Equal(new[] { false }, late);
        Assert.Equal(3, evaluations);
        cache.Dispose();
        Assert.False(source.HasObservers);
        source.OnNext(1);
        Assert.Equal(3, evaluations);
        Assert.Throws<ObjectDisposedException>(() => cache.Value);
        Assert.Throws<ObjectDisposedException>(() => cache.Subscribe(_ => { }));
    }

    /// <summary>A missing synchronous seed fails immediately and disconnects the failed source.</summary>
    [Fact]
    public void MissingInitialValueRejectsAndUnsubscribes()
    {
        // Arrange: a subject has no synchronous initial value.
        using var source = new Subject<int>();

        // Act: construction attempts to read the required initial value.
        Assert.Throws<ArgumentException>(() => new CachedObservable<int>(source));

        // Assert: the failed construction does not leave an upstream observer behind.
        Assert.False(source.HasObservers);
    }

    #endregion
}
