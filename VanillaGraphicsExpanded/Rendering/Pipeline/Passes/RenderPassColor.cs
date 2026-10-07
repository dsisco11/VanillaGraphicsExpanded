using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Maps one fragment output slot to an attachment; minus one preserves a sparse hole.</summary>
internal sealed record RenderPassColor(int Attachment, AttachmentLoad Load = AttachmentLoad.Preserve,
    AttachmentStore Store = AttachmentStore.Preserve, ColorClearValue? Clear = null, bool DiscardOutput = false,
    DrawBuffersEnum? SurfaceBuffer = null);
