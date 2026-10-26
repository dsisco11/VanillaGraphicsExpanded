using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Checks native activity excluded from complete graphics interruptions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Verifies transform feedback is inactive, including paused-but-active feedback.</summary>
    internal bool TryVerifyInactiveTransformFeedback()
    {
        RequireOutsideEngineBoundary();
        try
        {
            // Binding identity cannot establish whether feedback is capturing or paused.
            return !QueryBoundary(() => RHI.RHICommandContext.Current.OpenGL.ReadTransformFeedbackActive());
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            BoundaryEntryFailure = error;
            return false;
        }
    }
    #endregion
}
