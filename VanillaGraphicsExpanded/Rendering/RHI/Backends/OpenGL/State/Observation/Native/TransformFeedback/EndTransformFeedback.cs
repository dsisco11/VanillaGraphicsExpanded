using System;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glEndTransformFeedback after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glEndTransformFeedback")]
    [InterceptOpenGL("glEndTransformFeedbackEXT")]
    internal static void ObserveEndTransformFeedback()
    {
        OpenGlStateCache.ObserveFeedbackActivity(OpenGlStateCache.FeedbackActivityCommand.End);
    }
    #endregion
}
