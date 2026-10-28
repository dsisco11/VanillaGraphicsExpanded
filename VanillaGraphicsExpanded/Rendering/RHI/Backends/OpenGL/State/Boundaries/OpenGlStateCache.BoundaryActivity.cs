using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Checks native activity excluded from complete graphics interruptions.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Verifies transform feedback is inactive, including paused-but-active feedback.</summary>
    internal bool TryVerifyInactiveTransformFeedback()
    {
        RequireOutsideEngineBoundary();
        try
        {
            if (TryGetFeedbackActivity(out var known)) return !known.Active;
            return !QueryBoundary(() => OpenGLFeedbackBindings.GetExternalActivity(this).Active);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            BoundaryEntryFailure = error;
            return false;
        }
    }
    #endregion
}
