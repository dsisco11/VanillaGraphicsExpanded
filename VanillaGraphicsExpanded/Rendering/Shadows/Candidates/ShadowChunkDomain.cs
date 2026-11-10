using System;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>Declares the complete loaded envelope as inclusive native chunk indices.</summary>
internal readonly record struct ShadowChunkDomain(int Dimension, int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ)
{
    /// <summary>Bounds relative double arithmetic without truncating a larger declared domain.</summary>
    internal bool IsValid => MinX <= MaxX && MinY <= MaxY && MinZ <= MaxZ
        && (long)MaxX - MinX <= 1048576 && (long)MaxY - MinY <= 1048576 && (long)MaxZ - MinZ <= 1048576
        && MaxX < int.MaxValue && MaxY < int.MaxValue && MaxZ < int.MaxValue;
    /// <summary>Gets the closed partition envelope in chunk units.</summary>
    internal ShadowBounds Bounds => new(MinX, MinY, MinZ, (double)MaxX + 1, (double)MaxY + 1, (double)MaxZ + 1);
}
