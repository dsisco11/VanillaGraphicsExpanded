using System;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glResumeTransformFeedback after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glResumeTransformFeedback")]
    internal static void ObserveResumeTransformFeedback()
    {
        OpenGlStateCache.ObserveFeedbackActivity(OpenGlStateCache.FeedbackActivityCommand.Resume);
    }
    #endregion
}
