using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Defers publication across caller-owned command groups without owning their resource policy.</summary>
internal sealed partial class StateCache
{
    private readonly HashSet<GpuResource> metadataUpdates = new();

    #region Private
    /// <summary>Publishes native results immediately unless a complete command group is still being executed.</summary>
    private void ObserveMetadata<T>(GpuResource<T> resource, IGpuResourceMetadataReader<T> reader,
        GpuMetadataChange change) where T : struct
    {
        if (!metadataUpdates.Contains(resource)) GpuResourceManager.ObserveWrapperMetadata(resource, reader, change);
    }
    #endregion

    /// <summary>Withdraws availability until a caller completes every command in its resource update.</summary>
    internal sealed class MetadataUpdate<T> : IDisposable where T : struct
    {
        private readonly StateCache cache;
        private readonly GpuResource<T> resource;
        private readonly IGpuResourceMetadataReader<T> reader;
        private readonly GpuMetadataChange change;
        private bool completed;
        private bool disposed;

        #region Internal API
        /// <summary>Starts one non-nested update for this resource, keeping intermediate facts unavailable.</summary>
        internal MetadataUpdate(StateCache cache, GpuResource<T> resource, IGpuResourceMetadataReader<T> reader,
            GpuMetadataChange change)
        {
            this.cache = cache;
            this.resource = resource;
            this.reader = reader;
            this.change = change;
            if (!cache.metadataUpdates.Add(resource)) throw new InvalidOperationException("A metadata update is already active for this resource.");
            GpuResourceManager.InvalidateWrapperMetadata(resource, change);
        }

        #endregion

        #region Public API
        /// <summary>Admits publication after the caller has successfully issued every command in the group.</summary>
        public void Complete() => completed = true;

        /// <summary>Publishes once for a complete group; failed groups retain unavailable canonical metadata.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            cache.metadataUpdates.Remove(resource);
            if (completed) GpuResourceManager.ObserveWrapperMetadata(resource, reader, change);
            else GpuResourceManager.DiscardUntrackedMetadata(resource, change);
        }
        #endregion
    }
}
