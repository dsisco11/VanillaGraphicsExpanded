using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;

namespace VanillaGraphicsExpanded.LumOn.Scene.HitLighting;

/// <summary>Fixed ray-query storage matching the producer's maximum rays per texel.</summary>
[InlineArray(64)]
internal struct SurfaceHitQueries
{
    private SurfaceLightingQuery element;
}

/// <summary>One 4,176-byte GPU record; only complete geometry with missing lighting is retained.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SurfaceHitCapture
{
    public SurfaceFallbackRequest Request;
    public uint Complete, QueryCount, Reserved0, Reserved1;
    public SurfaceHitQueries Queries;
}
