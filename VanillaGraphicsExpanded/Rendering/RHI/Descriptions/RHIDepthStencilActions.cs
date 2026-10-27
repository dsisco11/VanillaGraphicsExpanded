namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Declares independent load/store intentions for depth and stencil, including packed images.</summary>
public sealed record RHIDepthStencilActions(AttachmentLoad DepthLoad = AttachmentLoad.Preserve,
    AttachmentStore DepthStore = AttachmentStore.Preserve, float ClearDepth = 1,
    AttachmentLoad StencilLoad = AttachmentLoad.Preserve, AttachmentStore StencilStore = AttachmentStore.Preserve,
    int ClearStencil = 0);
