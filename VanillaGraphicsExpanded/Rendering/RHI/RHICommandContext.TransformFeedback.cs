namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Provides explicit capture lifetime operations; native draws remain independently ordered.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Retains a complete capture interval and ends it during scope unwind.</summary>
    public System.IDisposable CaptureTransformFeedback(GpuTransformFeedbackRef feedback, RHICapturePrimitive primitive) => backend.CaptureTransformFeedback(feedback, primitive);
    /// <summary>Creates a capture owner from frozen output selections.</summary>
    public RHICreationResult<GpuTransformFeedback> CreateTransformFeedback(RHITransformFeedbackDescription description) => backend.CreateTransformFeedback(description);
    /// <summary>Begins capture and retains its binding until explicit end or scope unwind.</summary>
    public void BeginTransformFeedback(GpuTransformFeedbackRef feedback, RHICapturePrimitive primitive) => backend.BeginTransformFeedback(feedback, primitive);
    /// <summary>Ends the active capture and releases its retained output guards.</summary>
    public void EndTransformFeedback() => backend.EndTransformFeedback();
    /// <summary>Pauses capture without releasing its lifetime or surrounding selector restoration.</summary>
    public void PauseTransformFeedback() => backend.PauseTransformFeedback();
    /// <summary>Resumes paused capture.</summary>
    public void ResumeTransformFeedback() => backend.ResumeTransformFeedback();
    #endregion
}
