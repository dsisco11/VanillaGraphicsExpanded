using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace VanillaGraphicsExpanded.Rendering.Enablement;

/// <summary>Owns a connected Rx value with change suppression, synchronous reads and replay to late subscribers.</summary>
internal sealed class CachedObservable<T> : IObservable<T>, IDisposable
{
    private readonly IConnectableObservable<T> values;
    private readonly IDisposable cache;
    private readonly IDisposable connection;
    private T current = default!;
    private bool initialized, disposed;

    #region Public API
    /// <summary>Connects once for the owning lifetime; the source must synchronously supply an initial value.</summary>
    internal CachedObservable(IObservable<T> source)
    {
        values = source.DistinctUntilChanged().Replay(1);
        cache = values.Subscribe(value => { current = value; initialized = true; });
        connection = values.Connect();
        if (!initialized)
        {
            Dispose();
            throw new ArgumentException("A cached observable requires a synchronous initial value.", nameof(source));
        }
    }

    /// <summary>Returns the latest committed value without executing the source or its predicate.</summary>
    internal T Value => !disposed ? current : throw new ObjectDisposedException(GetType().Name);

    /// <summary>Immediately supplies the current value and then subsequent distinct changes.</summary>
    public IDisposable Subscribe(IObserver<T> observer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return values.Subscribe(observer);
    }

    /// <summary>Disconnects the upstream computation and the cached-value observer.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        connection?.Dispose();
        cache.Dispose();
    }
    #endregion
}
