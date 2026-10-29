using System;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBeginTransformFeedback after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBeginTransformFeedback")]
    [InterceptOpenGL("glBeginTransformFeedbackEXT")]
    internal static void ObserveBeginTransformFeedback(int a0)
    {
        OpenGlStateCache.ObserveFeedbackActivity(OpenGlStateCache.FeedbackActivityCommand.Begin, (OpenTK.Graphics.OpenGL.TransformFeedbackPrimitiveType)a0);
    }
    #endregion
}
