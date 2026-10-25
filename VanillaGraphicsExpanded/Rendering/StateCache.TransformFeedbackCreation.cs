using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes TransformFeedback creation without acquiring unrepresented metadata.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Requests TransformFeedback creation; configuration and use remain separate operations.</summary>
    internal GpuTransformFeedbackMetadata? CreateTransformFeedback(out nint id)
    {
        id = GL.GenTransformFeedback();
        // Empty metadata neither queries status nor certifies generated-name realization.
        return id == 0 ? null : new GpuTransformFeedbackMetadata();
    }
    #endregion
}
