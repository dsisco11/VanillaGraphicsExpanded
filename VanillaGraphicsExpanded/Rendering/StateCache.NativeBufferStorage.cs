using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Observes external buffer allocation commands without retaining upload data or introducing ownership.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Captures the original tracked object before an exact native storage call executes.</summary>
    internal static NativeBufferStorage BeginNativeBufferStorage(int selector, bool named)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true) return default;
        var cache = Current;
        int id = named ? selector : cache.GetBoundBuffer((BufferTarget)selector);
        var entry = manager.FindObservedResource(GpuResourceKind.Buffer, id)
            as GpuResourceMetadataEntry<GpuBufferMetadata>;
        // No registration is created for untracked engine objects. Existing aliases share
        // one readback, and an interrupted native dispatch cannot leave their old facts usable.
        if (entry is not null)
        {
            // Generated buffer names can be registered before their first binding realizes
            // the object. The first observed allocation must also establish their reader.
            entry.Reader ??= new GpuBufferMetadataReader();
            entry.BeginRefresh(GpuMetadataChange.Storage);
        }
        return new(entry);
    }
    #endregion

    /// <summary>Retains the original entry across native execution without capturing caller buffers.</summary>
    internal readonly struct NativeBufferStorage(GpuResourceMetadataEntry<GpuBufferMetadata>? entry)
    {
        #region Internal API
        /// <summary>Reads actual allocation facts while preserving an original native dispatch exception.</summary>
        internal void Complete(bool nativeReturned)
        {
            if (entry is null || !entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
            try
            {
                if (!entry.Manager.RefreshMetadata(entry, GpuMetadataChange.Storage))
                    throw new InvalidOperationException("Native buffer metadata is unavailable after storage mutation.");
            }
            catch when (!nativeReturned)
            {
                // Refresh already withdrew availability. Preserve the original exception
                // rather than replacing it with a secondary metadata acquisition failure.
            }
        }
        #endregion
    }
}
