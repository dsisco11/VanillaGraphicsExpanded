using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Immutable format support for one image target and sized internal format.</summary>
internal sealed record InternalFormatCapabilities
{
    /// <summary>Whether this format can be allocated for the queried target.</summary>
    public bool Supported { get; init; }
    /// <summary>Target-specific shader image load support; zero when unavailable or not queried.</summary>
    public int ImageLoadSupport { get; init; }
    /// <summary>Target-specific shader image store support; zero when unavailable or not queried.</summary>
    public int ImageStoreSupport { get; init; }
    /// <summary>Hardware filtering support for ordinary 2D/3D textures; zero for other queried targets.</summary>
    public int FilterSupport { get; init; }
    /// <summary>Native framebuffer-renderability support: None, FullSupport or CaveatSupport from OpenGL.</summary>
    public int FramebufferRenderableSupport { get; init; }
    /// <summary>Supported multisample counts in driver order; empty for non-multisample texture targets.</summary>
    public ImmutableArray<int> SampleCounts { get; init; } = [];
}
