using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes MemoryObject creation without acquiring unrepresented metadata.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Requests MemoryObject creation; configuration and use remain separate operations.</summary>
    internal GpuMemoryObjectMetadata? CreateMemoryObject(out nint id)
    {
        GL.Ext.CreateMemoryObjects(1, out int nativeId);
        id = nativeId;
        return id == 0 ? null : new GpuMemoryObjectMetadata();
    }
    #endregion
}
