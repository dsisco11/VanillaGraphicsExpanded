using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the complete std140 layout for lumon scene relight voxel dda parameters.</summary>
internal sealed class LumonSceneRelightVoxelDdaParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 80;
    internal const int AtlasLayoutOffsetBytes = 0;
    internal const int RelightUints0OffsetBytes = 16;
    internal const int RelightInts0OffsetBytes = 32;
    internal const int OccOriginMinCell0OffsetBytes = 48;
    internal const int OccRing0OffsetBytes = 64;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;
    /// <summary>Stages the complete atlas mapping lane without changing other dispatch fields.</summary>
    internal void SetAtlasLayout(uint tileSize, uint tilesPerAxis, uint tilesPerAtlas, uint border)
    {
        WriteUIntVector4(AtlasLayoutOffsetBytes, tileSize, tilesPerAxis, tilesPerAtlas, border);
    }

    /// <summary>Stages budgets while retaining the independently supplied occupancy resolution.</summary>
    internal void SetRelight(uint texels, uint rays, uint steps, bool diagnostics)
    {
        int resolution = UboPacking.ReadInt32(DataReadOnly, RelightInts0OffsetBytes + sizeof(int));
        WriteUIntVector4(RelightUints0OffsetBytes, texels, rays, steps, diagnostics ? 1u : 0u);
        WriteIntVector4(RelightInts0OffsetBytes, 0, resolution, 0, 0);
    }

    /// <summary>Stages one complete occupancy mapping without replacing atlas or budget fields.</summary>
    internal void SetOccupancy(int x, int y, int z, int ringX, int ringY, int ringZ, int resolution)
    {
        WriteIntVector4(OccOriginMinCell0OffsetBytes, x, y, z, 0);
        WriteIntVector4(OccRing0OffsetBytes, ringX, ringY, ringZ, 0);
        WriteIntVector4(RelightInts0OffsetBytes, 0, resolution, 0, 0);
    }
    #endregion
}
