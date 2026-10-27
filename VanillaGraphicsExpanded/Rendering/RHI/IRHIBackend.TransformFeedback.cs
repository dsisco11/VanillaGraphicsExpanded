namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines complete transform-feedback creation and capture operations.</summary>
internal partial interface IRHIBackend
{
    /// <summary>Begins capture with guaranteed matching end on explicit scope disposal.</summary>
    System.IDisposable CaptureTransformFeedback(GpuTransformFeedbackRef feedback, CapturePrimitive primitive);
    /// <summary>Creates and configures an existing owner before exposing it.</summary>
    RHICreationResult<GpuTransformFeedback> CreateTransformFeedback(RHITransformFeedbackDescription description);
    /// <summary>Begins a capture interval retaining its original outputs until matching end/unwind.</summary>
    void BeginTransformFeedback(GpuTransformFeedbackRef feedback, CapturePrimitive primitive);
    /// <summary>Ends the active capture and restores surrounding object selection.</summary>
    void EndTransformFeedback();
    /// <summary>Pauses the matching active capture.</summary>
    void PauseTransformFeedback();
    /// <summary>Resumes the matching paused capture.</summary>
    void ResumeTransformFeedback();
}
