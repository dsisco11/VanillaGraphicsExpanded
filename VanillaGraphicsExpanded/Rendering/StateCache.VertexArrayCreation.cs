using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes VertexArray creation without acquiring unrepresented metadata.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Requests VertexArray creation; configuration and use remain separate operations.</summary>
    internal GpuVertexArrayMetadata? CreateVertexArray(out nint id)
    {
        int nativeId;
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess) GL.CreateVertexArrays(1, out nativeId);
        else nativeId = GL.GenVertexArray();
        id = nativeId;
        if (id == 0) return null;
        // Realize fallback names while preserving the caller's selection.
        if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
        {
            using var binding = BindVertexArrayScope(nativeId);
        }
        return new GpuVertexArrayMetadata();
    }
    #endregion
}
