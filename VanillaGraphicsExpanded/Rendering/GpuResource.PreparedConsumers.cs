using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Delivers committed changes to derived preparation without extending consumer lifetimes.</summary>
public abstract partial class GpuResource
{
    private ConditionalWeakTable<object, Action>? preparedConsumers;

    #region Internal API
    /// <summary>Associates actual cached preparation weakly; abandoned commands need no explicit unsubscription.</summary>
    internal void ObservePreparation(object owner, Action invalidate) => LazyInitializer.EnsureInitialized(ref preparedConsumers).AddOrUpdate(owner, invalidate);

    /// <summary>Runs wrapper policy and invalidates dependent preparation even when that policy reports failure.</summary>
    internal void NotifyResourceChanged(in RHIResourceChange change)
    {
        try { OnResourceChanged(change); }
        finally
        {
            // Callbacks only withdraw derived facts. They never query, allocate or repair resources.
            InvalidatePreparedConsumers();
        }
    }
    /// <summary>Withdraws local derived selections before an owner retires its wrapper identity.</summary>
    private void InvalidatePreparedConsumers()
    {
        var consumers = Volatile.Read(ref preparedConsumers);
        if (consumers is null) return;
        foreach (var consumer in consumers) consumer.Value();
    }
    #endregion
}
