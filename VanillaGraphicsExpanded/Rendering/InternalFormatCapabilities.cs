using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Immutable format support for one image target and sized internal format.</summary>
internal sealed record InternalFormatCapabilities
{
    /// <summary>Whether this format can be allocated for the queried target.</summary>
    public bool Supported { get; init; }
    /// <summary>Native framebuffer-renderability support: None, FullSupport or CaveatSupport from OpenGL.</summary>
    public int FramebufferRenderableSupport { get; init; }
    /// <summary>Supported multisample counts in driver order; empty for non-multisample texture targets.</summary>
    public ImmutableArray<int> SampleCounts { get; init; } = [];
}
