using System.Reactive.Linq;
using System.Reactive.Subjects;
using Moq;
using VanillaGraphicsExpanded.Rendering.Enablement;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Qualifies shared cached Rx lifetimes and rendering-thread delivery.</summary>
public sealed class CachedObservableTests
{
    #region Public API
    /// <summary>One upstream computation supplies cached reads and late subscribers without duplicate changes.</summary>
    [Fact]
    public void CachedValueSharesConnectionSuppressesDuplicatesAndDetaches()
    {
        using var source = new BehaviorSubject<int>(1);
        int evaluations = 0;
        using var cache = new CachedObservable<bool>(source.Select(value => { evaluations++; return value > 0; }));
        var first = new List<bool>();
        using var firstSubscription = cache.Subscribe(first.Add);
        Assert.True(cache.Value);
        Assert.True(cache.Value);
        source.OnNext(2);
        source.OnNext(0);
        var late = new List<bool>();
        using var lateSubscription = cache.Subscribe(late.Add);
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
        using var source = new Subject<int>();
        Assert.Throws<ArgumentException>(() => new CachedObservable<int>(source));
        Assert.False(source.HasObservers);
    }

    /// <summary>Background changes enter the native queue and become inert when their owner retires.</summary>
    [Fact]
    public void RenderingDispatchQueuesBackgroundWorkAndCancelsRetiredWork()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var queued = new List<Action>();
        api.Setup(value => value.Event.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((action, _) => queued.Add(action));
        using var dispatch = new RenderThreadDispatch(api.Object);
        int calls = 0;
        dispatch.Run(() => calls++);
        Assert.Equal(1, calls);
        var thread = new Thread(() => dispatch.Run(() => calls++));
        thread.Start(); thread.Join();
        Assert.Equal(1, calls);
        Assert.Single(queued)();
        Assert.Equal(2, calls);
        var pending = new Thread(() => dispatch.Run(() => calls++));
        pending.Start(); pending.Join();
        dispatch.Dispose();
        queued[1]();
        dispatch.Run(() => calls++);
        Assert.Equal(2, calls);
    }
    #endregion
}
