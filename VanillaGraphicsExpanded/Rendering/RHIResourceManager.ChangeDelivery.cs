using System;
using System.Collections.Generic;
namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Completes dependency facts synchronously before delivering consumer changes.</summary>
internal sealed partial class RHIResourceManager
{
    private List<Action>? changeDeliveries;
    private HashSet<GpuResource>? notifiedOwners;

    #region Internal API
    /// <summary>Opens a completion-local delivery batch; recursive dependency publication joins it.</summary>
    internal ChangeDelivery BeginChangeDelivery()
    {
        int owner = System.Threading.Volatile.Read(ref metadataThreadId);
        if (owner != 0 && owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Resource completion belongs to its originating context thread.");
        return new(this);
    }

    /// <summary>Coalesces dependency fanout to an owner within one completed change chain.</summary>
    internal bool AdmitDependentOwnerChange(GpuResource owner) => notifiedOwners?.Add(owner) ?? true;

    /// <summary>Defers consumer work until the outer completion has finalized its outcome.</summary>
    internal void DeliverResourceChange(Action delivery)
    {
        if (changeDeliveries is { } deliveries) deliveries.Add(delivery);
        else delivery();
    }
    #endregion

    /// <summary>Owns one synchronous completion's callbacks, not an asynchronous event queue.</summary>
    internal sealed class ChangeDelivery : IDisposable
    {
        private readonly RHIResourceManager manager;
        private readonly bool outer;
        private bool successful;
        #region Internal API
        /// <summary>Joins an existing dependency completion or creates its short-lived delivery list.</summary>
        internal ChangeDelivery(RHIResourceManager manager)
        {
            this.manager = manager;
            outer = manager.changeDeliveries is null;
            manager.changeDeliveries ??= new();
        }
        /// <summary>Allows notification failures to be reported only after successful completion.</summary>
        internal void Complete() => successful = true;
        #endregion
        #region Public API
        /// <summary>Attempts all committed callbacks, preserving any primary mutation exception.</summary>
        public void Dispose()
        {
            if (!outer) return;
            var deliveries = manager.changeDeliveries!;
            manager.changeDeliveries = null;
            List<Exception>? failures = null;
            var previousOwners = manager.notifiedOwners;
            manager.notifiedOwners = new();
            try
            {
                foreach (var delivery in deliveries)
                    try { delivery(); } catch (Exception error) { (failures ??= new()).Add(error); }
            }
            finally { manager.notifiedOwners = previousOwners; }
            if (successful && failures is not null) throw new AggregateException("Resource change notification failed after commit.", failures);
        }
        #endregion
    }
}
