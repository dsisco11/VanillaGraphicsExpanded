using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Owns the complete std140 layout for celestial generation inputs.</summary>
internal sealed class CelestialGenerationInputs : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 64;
    internal const int TileOffset = 0;
    internal const int CatalogOffset = 16;
    internal const int SourceOffset = 32;
    internal const int FilterOffset = 48;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Packs the complete tile, source and filter inputs before publishing their revision.</summary>
    internal void SetTile(int face, int size, int x, int y, int indexOffset, int indexCount,
        int parentLevel, int parentSize, CelestialSource source)
    {
        Span<byte> values = stackalloc byte[BlockSize];
        UboPacking.WriteVec4(values, TileOffset, face, size, x, y);
        UboPacking.WriteVec4(values, CatalogOffset, indexOffset, indexCount, parentLevel, parentSize);
        UboPacking.WriteVec4(values, SourceOffset, source.GalaxyAmplitude, source.StorageScale, source.Quality.Octaves, source.MaximumNits);
        UboPacking.WriteVec4(values, FilterOffset, CelestialResidual.Height, CelestialResidual.KernelMaximum, 0, 0);
        SetBytes(values);
    }
    #endregion
}
