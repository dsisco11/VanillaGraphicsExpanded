using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Preserves exact external capture selection without managed duplicate suppression.</summary>
internal static class OpenGLFeedbackCalls
{
    #region Public API
    /// <summary>Executes the original selector once, withdrawing uncertain binding knowledge.</summary>
    public static void BindTransformFeedback(TransformFeedbackTarget target, int feedback)
    {
        var cache = EngineStateCalls.CurrentCache;
        cache.TransformFeedbackKnowledge = null;
        GL.BindTransformFeedback(target, feedback);
    }
    #endregion
}
