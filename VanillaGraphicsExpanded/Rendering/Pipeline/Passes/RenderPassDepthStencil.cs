namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Declares independent load/store intentions for depth and stencil, including packed images.</summary>
internal sealed record RenderPassDepthStencil(AttachmentLoad DepthLoad = AttachmentLoad.Preserve,
    AttachmentStore DepthStore = AttachmentStore.Preserve, float ClearDepth = 1,
    AttachmentLoad StencilLoad = AttachmentLoad.Preserve, AttachmentStore StencilStore = AttachmentStore.Preserve,
    int ClearStencil = 0);
