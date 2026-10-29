using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes native attachments and attachmentless defaults without duplicating routing state.</summary>
public readonly record struct GpuFramebufferMetadata(
    ImmutableDictionary<FramebufferSlot, GpuFramebufferImageMetadata> Attachments,
    int DefaultWidth, int DefaultHeight, int DefaultLayers, int DefaultSamples, bool DefaultFixedSampleLocations);

/// <summary>Identifies the native image selected by an attachment; image facts resolve through its registration.</summary>
public readonly record struct GpuFramebufferImageMetadata(
    GpuResourceKind Kind, nint Id, int MipLevel, int Layer, CubeFace CubeFace,
    bool Layered, int BaseView = 0, int ViewCount = 0,
    GpuFramebufferAttachmentMode Mode = GpuFramebufferAttachmentMode.Unknown)
{
    /// <summary>Retains the original backing identity when its numeric name is later reused.</summary>
    internal GpuResourceRegistration? OriginalBacking { get; init; }
}

/// <summary>Distinguishes proven attachment command semantics from ambiguous native selection queries.</summary>
public enum GpuFramebufferAttachmentMode { Unknown, Ordinary, Multiview }
