using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes Query creation without acquiring unrepresented metadata.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Requests Query creation; configuration and use remain separate operations.</summary>
    internal GpuQueryMetadata? CreateQuery(out nint id)
    {
        id = GL.GenQuery();
        // Empty metadata neither queries status nor certifies generated-name realization.
        return id == 0 ? null : new GpuQueryMetadata();
    }
    #endregion
}
