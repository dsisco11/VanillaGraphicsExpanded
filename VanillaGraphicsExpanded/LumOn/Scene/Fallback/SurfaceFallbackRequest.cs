using System.Numerics;
using System.Runtime.InteropServices;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene.Fallback;

/// <summary>One 64-byte texel retry retaining an integer origin and the original deterministic ray seed.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SurfaceFallbackRequest
{
    public uint Page, Slot, Patch, Linear;
    public int X, Y, Z;
    public uint Seed;
    public Vector4 Fraction;
    public Vector4 Normal; // xyz: surface normal; w: maximum trace distance in blocks.
}

/// <summary>One transactional texel result; unresolved rays never publish a partial ray denominator.</summary>
internal readonly record struct SurfaceFallbackTexel(SurfaceFallbackRequest Request, int FirstQuery, int QueryCount, bool Complete,
    bool Exhausted = false);

/// <summary>Retains the exact loaded chunk identity observed by the collision worker.</summary>
internal readonly record struct SurfaceFallbackDependency(VectorInt3 Chunk, object? Identity);

/// <summary>Transfers bounded immutable collision results back to the render thread.</summary>
internal sealed record SurfaceFallbackResult(ImmutableArray<SurfaceFallbackTexel> Texels,
    ImmutableArray<SurfaceLightingQuery> Queries, ImmutableArray<SurfaceFallbackDependency> Dependencies);

/// <summary>One 32-byte validated estimate addressed to an exact physical page texel.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SurfaceFallbackCommit
{
    public uint Page, Slot, Patch, Linear;
    public Vector4 Estimate;
}
