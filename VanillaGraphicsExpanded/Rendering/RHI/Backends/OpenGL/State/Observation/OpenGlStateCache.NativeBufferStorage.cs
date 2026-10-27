using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Withdraws uncertain external buffer storage until a consumer explicitly acquires its descriptor.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Invalidates only the selected original allocation before exact external storage dispatch.</summary>
    internal static NativeBufferStorage InvalidateNativeBufferStorage(int selector, bool named)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true && Current.resourceOperation is null) return default;
        int id = named ? selector : Current.GetBoundBuffer((BufferTarget)selector);
        Current.RequireResourceOperationAvailable(GpuResourceKind.Buffer, id);
        if (manager?.CanObserveNativeMetadata != true) return default;
        var entry = manager.FindObservedResource(GpuResourceKind.Buffer, id) as GpuResourceMetadataEntry<GpuBufferMetadata>;
        if (entry is null) return default;
        if (entry.CompletionInProgress)
        {
            entry.CompletionInterrupted = true;
            throw new System.InvalidOperationException("Buffer metadata completion is in progress.");
        }
        // Neither a returned native call nor equal descriptor values prove accepted storage
        // replacement. Keep the storage effect pending for explicit acquisition, including
        // interruption; no completion read or eager registration is needed here.
        entry.Reader ??= new GpuBufferMetadataReader();
        var guard = Current.EnterResourceOperation(entry.Handle);
        entry.OperationInProgress = true;
        entry.BeginRefresh(GpuMetadataChange.Storage);
        return new(Current, entry, guard);
    }
    #endregion

    /// <summary>Guards the original unavailable entry until exact dispatch finishes, without acquiring a result.</summary>
    internal readonly struct NativeBufferStorage(OpenGlStateCache cache, GpuResourceMetadataEntry<GpuBufferMetadata> entry,
        ResourceOperationGuard guard)
    {
        #region Internal API
        /// <summary>Releases only execution bookkeeping and retains pending storage recovery on success or interruption.</summary>
        internal void Complete()
        {
            if (entry is null) return;
            try
            {
                if (entry.Manager.IsRegistered(entry) && entry.State == GpuResourceRegistrationState.Live)
                    entry.BeginRefresh(GpuMetadataChange.Storage);
            }
            finally
            {
                entry.OperationInProgress = false;
                cache.ExitResourceOperation(guard);
            }
        }
        #endregion
    }
}
