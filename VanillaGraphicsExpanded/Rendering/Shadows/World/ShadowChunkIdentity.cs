using System;

namespace VanillaGraphicsExpanded.Rendering.Shadows.World;

/// <summary>Preserves native dimension and fixed-grid coordinates independently of render origin.</summary>
internal readonly record struct ShadowChunkIdentity(int Dimension, int X, int Y, int Z)
{
    #region Public API
    /// <summary>Produces bounded relative arithmetic without changing the absolute identity.</summary>
    internal bool TryRelative(int originX, int originY, int originZ, out (int X, int Y, int Z) relative)
    {
        long x = (long)X - originX, y = (long)Y - originY, z = (long)Z - originZ;
        relative = default;
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue || z < int.MinValue || z > int.MaxValue) return false;
        relative = ((int)x, (int)y, (int)z);
        return true;
    }

    /// <summary>Defines the CPU/GPU lookup hash with explicit wrapping unsigned arithmetic.</summary>
    internal uint Hash()
    {
        unchecked
        {
            uint hash = 2166136261;
            hash = (hash ^ (uint)Dimension) * 16777619;
            hash = (hash ^ (uint)X) * 16777619;
            hash = (hash ^ (uint)Y) * 16777619;
            return (hash ^ (uint)Z) * 16777619;
        }
    }
    #endregion
}
