using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes Semaphore creation without acquiring unrepresented metadata.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Requests Semaphore creation; configuration and use remain separate operations.</summary>
    internal GpuSemaphoreMetadata? CreateSemaphore(out nint id)
    {
        id = GL.Ext.GenSemaphore();
        // Import and signaling state are deliberately absent from the canonical schema.
        return id == 0 ? null : new GpuSemaphoreMetadata();
    }
    #endregion
}
