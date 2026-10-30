using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns original identity and availability for one explicitly composed native operation.</summary>
internal sealed partial class RHIResourceManager
{
    /// <summary>Captures one operation's target; callers supply its complete result explicitly.</summary>
    internal sealed class MetadataOperation<T> : IDisposable where T : struct
    {
        private readonly GpuResource<T> resource;
        private readonly GpuResourceMetadataEntry<T>? entry;
        private readonly (nint Handle, long Generation) context;
        private readonly IRHIResourceOperation guard;
        private GpuMetadataChange effects;
        private bool completed, disposed, completionAttempted;

        #region Public API
        /// <summary>Releases bookkeeping without reading native state or treating disposal as success.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (entry is not null) entry.OperationInProgress = false;
                if (!completed)
                {
                    if (entry is null)
                    {
                        if (!guard.Deleted && resource.ResourceId == guard.Handle.Id)
                            try { InvalidateWrapperMetadata(resource, effects); } catch { }
                    }
                    else
                    {
                        using var delivery = entry.Manager.BeginChangeDelivery();
                        entry.Withdraw(effects, resource);
                        if (entry.Kind == GpuResourceKind.Texture) entry.Manager.WithdrawResourceDependencies(entry, GpuMetadataChange.Metadata);
                    }
                }
            }
            finally { guard.Dispose(); }
        }
        #endregion

        #region Internal API
        /// <summary>Withdraws affected metadata before any participating command executes.</summary>
        internal MetadataOperation(GpuResource<T> resource, GpuMetadataChange effects)
        {
            ObjectDisposedException.ThrowIf(!resource.IsValid, resource);
            resource.RequireOriginalContext();
            this.resource = resource;
            this.effects = effects;
            context = RenderContextRegistry.Current();
            var manager = resource.ResourceManager;
            if (manager is not null && manager.CanAcquireWrapperMetadata(resource))
            {
                manager.RequireOriginalWrapper(resource);
                entry = manager.GetResourceRegistration(resource) as GpuResourceMetadataEntry<T>
                    ?? throw new InvalidOperationException("Resource operations require the original observed wrapper association.");
                manager.RequireLiveEntry(entry);
                if (entry.CompletionInProgress)
                {
                    entry.CompletionInterrupted = true;
                    entry.BeginRefresh(effects);
                    throw new InvalidOperationException("Metadata completion is already in progress.");
                }
            }
            guard = RHICommandContext.EnterResourceOperation(new(resource.Kind, resource.ResourceId));
            try
            {
                if (entry is not null)
                {
                    entry.OperationInProgress = true;
                    entry.BeginRefresh(effects);
                    if (!entry.ContainsRecipient(resource)) entry.Manager.AssociateResource(entry, resource);
                }

            }
            catch
            {
                if (entry is not null) entry.OperationInProgress = false;
                guard.Dispose();
                throw;
            }
        }

        /// <summary>Checks explicit participation before dispatch; effects remain accumulated after failure.</summary>
        internal void RequireParticipation(GpuResource<T> target, GpuMetadataChange change)
        {
            effects |= change;
            RequireLive();
            if (!ReferenceEquals(target, resource))
            {
                Interrupt();
                throw new InvalidOperationException("This step does not belong to the operation's target.");
            }
        }

        /// <summary>Prevents a caught participating dispatch failure from certifying the enclosing result.</summary>
        internal void Interrupt() => guard.Interrupt();

        /// <summary>Commits a complete supplied candidate once; native recovery is the operation owner's choice.</summary>
        internal void Commit(T candidate)
        {
            using var delivery = resource.ResourceManager?.BeginChangeDelivery();
            RequireLive();
            completionAttempted = true;
            if (entry is not null)
            {
                entry.Manager.PublishMetadataCore(entry, candidate, effects, false, guard, resource, MarkCommitted);
            }
            RHIResourceChange localChange = default;
            if (entry is null) localChange = DeliverUntrackedOperationResult(resource, candidate, effects);
            completed = true;
            if (entry is not null) entry.OperationInProgress = false;
            else
            {
                PublishOwnerImages(resource, true, reportMissingImages: (effects & GpuMetadataChange.Storage) != 0);
                if (localChange.Kind != ResourceChangeKind.None) resource.OnResourceChanged(localChange);
            }
            delivery?.Complete();
        }

        /// <summary>Checks original identity after any external callback or native read.</summary>
        internal void RequireLive()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed || completionAttempted || guard.Cancelled || context != RenderContextRegistry.Current()
                || !resource.IsValid || resource.ResourceId != guard.Handle.Id
                || entry is not null && (!entry.Manager.IsRegistered(entry)
                    || entry.State != GpuResourceRegistrationState.Live))
                throw new InvalidOperationException("The original operation is no longer available for completion.");
            // Preserve terminal-operation rejection above, then recheck affinity before
            // a participating dispatch or commit that could still use the live identity.
            resource.RequireOriginalContext();
            entry?.Manager.RequireMetadataContext();
        }
        #endregion

        #region Private
        /// <summary>Finalizes successful allocation before dependency errors or callbacks can unwind the operation.</summary>
        private void MarkCommitted()
        {
            completed = true;
            if (entry is not null) entry.OperationInProgress = false;
        }

        #endregion
    }
}
