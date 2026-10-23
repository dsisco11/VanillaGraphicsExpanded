using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes native attachments and attachmentless defaults without duplicating routing state.</summary>
public readonly record struct GpuFramebufferMetadata(
    ImmutableDictionary<FramebufferAttachment, GpuFramebufferImageMetadata> Attachments,
    int DefaultWidth, int DefaultHeight, int DefaultLayers, int DefaultSamples, bool DefaultFixedSampleLocations);

/// <summary>Identifies the native image selected by an attachment; image facts resolve through its registration.</summary>
public readonly record struct GpuFramebufferImageMetadata(
    GpuResourceKind Kind, nint Id, int MipLevel, int Layer, TextureTarget CubeFace,
    bool Layered, int BaseView = 0, int ViewCount = 0);
