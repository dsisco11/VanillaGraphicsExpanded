using System;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns original identity and availability for one explicitly composed native operation.</summary>
internal sealed partial class GpuResourceManager
{
    /// <summary>Captures one operation's target; callers supply its complete result explicitly.</summary>
    internal sealed class MetadataOperation<T> : IDisposable where T : struct
    {
        private readonly GpuResource<T> resource;
        private readonly GpuResourceMetadataEntry<T>? entry;
        private readonly (nint Handle, long Generation) context;
        private readonly StateCache cache;
        private readonly StateCache.ResourceOperationGuard guard;
        private GpuMetadataChange effects;
        private bool completed, disposed, completionAttempted;

        #region Internal API
        /// <summary>Withdraws affected metadata before any participating command executes.</summary>
        internal MetadataOperation(GpuResource<T> resource, GpuMetadataChange effects)
        {
            ObjectDisposedException.ThrowIf(!resource.IsValid, resource);
            this.resource = resource;
            this.effects = effects;
            context = RenderContextRegistry.Current();
            cache = StateCache.Current;
            var manager = resource.ResourceManager;
            if (manager is not null && manager.CanAcquireWrapperMetadata(resource))
            {
                manager.RequireOriginalWrapper(resource);
                entry = manager.GetResourceRegistration(resource) as GpuResourceMetadataEntry<T>
                    ?? manager.RegisterCreatedResource<T>(resource.Kind, resource.ResourceId);
                manager.RequireLiveEntry(entry);
                if (entry.CompletionInProgress)
                {
                    entry.CompletionInterrupted = true;
                    entry.BeginRefresh(effects);
                    throw new InvalidOperationException("Metadata completion is already in progress.");
                }
            }
            guard = cache.EnterResourceOperation(new(resource.Kind, resource.ResourceId));
            try
            {
                if (entry is not null)
                {
                    entry.OperationInProgress = true;
                    entry.BeginRefresh(effects);
                    if (!entry.ContainsRecipient(resource)) entry.Manager.AssociateResource(entry, resource);
                }
                else DiscardUntrackedMetadata(resource, GpuMetadataChange.None);
            }
            catch
            {
                if (entry is not null) entry.OperationInProgress = false;
                cache.ExitResourceOperation(guard);
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
        internal void Interrupt() => guard.Cancelled = true;

        /// <summary>Commits a complete supplied candidate once; native recovery is the operation owner's choice.</summary>
        internal void Commit(T candidate)
        {
            RequireLive();
            completionAttempted = true;
            if (entry is not null)
            {
                entry.Manager.PublishMetadataCore(entry, candidate, effects, false, guard);
                NotifyOperationDependents(entry, resource);
            }
            else DeliverUntrackedOperationResult(resource, candidate, effects);
            completed = true;
            if (entry is not null) entry.OperationInProgress = false;
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
        }
        #endregion

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
                        if (!guard.Deleted && resource.ResourceId == guard.Handle.Id) DiscardUntrackedMetadata(resource, effects);
                    }
                    else
                    {
                        entry.BeginRefresh(effects);
                        WithdrawOperationDependents(entry, resource, effects);
                    }
                }
            }
            finally { cache.ExitResourceOperation(guard); }
        }
        #endregion
    }
}
