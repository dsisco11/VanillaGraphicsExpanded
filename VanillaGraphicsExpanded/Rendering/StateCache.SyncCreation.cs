using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes Sync creation without acquiring unrepresented metadata.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Requests Sync creation; configuration and use remain separate operations.</summary>
    internal GpuSyncMetadata? CreateSync(out nint id)
    {
        id = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);
        // Keep the pointer-width identity; status is queried only by subsequent wait operations.
        return id == 0 ? null : new GpuSyncMetadata();
    }
    #endregion
}
