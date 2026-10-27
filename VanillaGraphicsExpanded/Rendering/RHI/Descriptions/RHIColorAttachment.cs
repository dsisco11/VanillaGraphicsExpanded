namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Declares one fragment output's image or explicit external route and independent load/store/clear intentions.</summary>
public sealed record RHIColorAttachment(RHIAttachment? Image = null, int? AttachmentIndex = null,
    AttachmentLoad Load = AttachmentLoad.Preserve, AttachmentStore Store = AttachmentStore.Preserve,
    ColorClearValue? Clear = null, bool DiscardOutput = false, SurfaceColorBuffer? WindowBuffer = null);
