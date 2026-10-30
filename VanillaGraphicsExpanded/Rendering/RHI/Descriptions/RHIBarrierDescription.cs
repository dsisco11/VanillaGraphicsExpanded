namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Names the subsequent accesses requiring visibility of earlier GPU writes.</summary>
/// <param name="Access">Subsequent accesses requiring visibility of shader memory writes.</param>
/// <param name="FramebufferFeedback">Orders prior framebuffer writes before subsequent texture sampling while images remain attached. Does not permit simultaneous writable feedback.</param>
public readonly record struct RHIBarrierDescription(ResourceAccess Access, bool FramebufferFeedback = false);
