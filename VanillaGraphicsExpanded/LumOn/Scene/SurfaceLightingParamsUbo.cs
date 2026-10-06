using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Packs the borrowed cache generation's sampling domain without publishing GPU state.</summary>
internal sealed class SurfaceLightingParamsUbo : CpuUniformBuffer
{
    #region Public API
    /// <summary>Creates an unavailable cache domain with the full shared block layout.</summary>
    public SurfaceLightingParamsUbo() : base(144)
    {
        // Lookup consumers sample existing radiance rather than executing its lighting producer.
        // Keep producer-only sampling, policy and atmosphere fields at their zero defaults, but
        // provide the complete linked block size even when those members are unused by lookup.
    }

    /// <summary>Replaces the complete domain, including unavailable snapshots.</summary>
    public void Set(SurfaceLightingSnapshot? snapshot)
    {
        var value = snapshot.GetValueOrDefault();
        WriteUIntVector4(0, (uint)value.TileSize, (uint)value.TilesPerAxis, (uint)value.TilesPerAtlas, 0);
        WriteIntVector4(32, value.Origin.X, value.Origin.Y, value.Origin.Z, 0);
        WriteIntVector4(48, value.Dimensions.X, value.Dimensions.Y, value.Dimensions.Z, 0);
        WriteIntVector4(64, value.Ring.X, value.Ring.Y, value.Ring.Z, 0);
    }
    #endregion
}
