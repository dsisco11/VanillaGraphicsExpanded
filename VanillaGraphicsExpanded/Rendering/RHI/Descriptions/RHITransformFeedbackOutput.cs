namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Associates one capture slot with an explicit output range.</summary>
public readonly record struct RHITransformFeedbackOutput(int Slot, RHIBufferRange Range);
