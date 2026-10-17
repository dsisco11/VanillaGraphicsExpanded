using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Describes owned coarse mips above borrowed native leaves; distances are world units.</summary>
internal sealed class CloudHierarchyLayout
{
    #region Public API
    #region Storage geometry
    /// <summary>The installed native GPU producer and admitted storage use this horizontal cell size.</summary>
    internal const int CellSize = 50;
    /// <summary>Gets the actual native square width, excluding logical padding.</summary>
    internal int NativeWidth { get; }
    /// <summary>Gets the power-of-two logical leaf width; no owned texture is allocated at this level.</summary>
    internal int LeafWidth { get; }
    /// <summary>Gets the number of owned coarse mips, excluding native leaves.</summary>
    internal int MipCount { get; }
    /// <summary>Gets total coarse RG32F texel payload bytes, excluding allocator overhead.</summary>
    internal int PayloadBytes { get; }

    /// <summary>Defines a layout only for the native dimensions admitted by the existing map adapter.</summary>
    internal CloudHierarchyLayout(int nativeWidth)
    {
        if (nativeWidth is < 20 or > 200) throw new ArgumentOutOfRangeException(nameof(nativeWidth));
        NativeWidth = nativeWidth;
        LeafWidth = (int)BitOperations.RoundUpToPowerOf2((uint)nativeWidth);
        MipCount = BitOperations.Log2((uint)LeafWidth);
        // The native texture supplies logical level zero. Owned storage starts one level above it.
        for (int width = LeafWidth / 2; width > 0; width /= 2) PayloadBytes += width * width * 2 * sizeof(float);
    }
    #endregion

    #region Node footprints
    /// <summary>Gets a coarse mip's square width, including empty logical padding.</summary>
    internal int MipWidth(int mip)
    {
        ValidateMip(mip);
        return LeafWidth >> (mip + 1);
    }

    /// <summary>Gets native cells per node axis; owned mip zero covers two cells, not one.</summary>
    internal int CellsPerNode(int mip)
    {
        ValidateMip(mip);
        return 1 << (mip + 1);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Rejects a leaf-level or out-of-chain lookup before shifting mip coordinates.</summary>
    private void ValidateMip(int mip)
    {
        if ((uint)mip >= (uint)MipCount) throw new ArgumentOutOfRangeException(nameof(mip));
    }
    #endregion
}
