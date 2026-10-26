using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns explicit external capture-activity discovery for engine boundary admission.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Reads actual active state, including paused capture, without inferring it from binding identity.</summary>
    internal bool ReadTransformFeedbackActive()
    {
        RequireContext();
        bool active = GL.GetBoolean((GetPName)All.TransformFeedbackActive);
        RequireContext();
        return active;
    }
    #endregion
}
