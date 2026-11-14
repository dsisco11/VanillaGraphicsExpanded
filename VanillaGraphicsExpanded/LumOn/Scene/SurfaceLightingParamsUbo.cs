using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Packs the borrowed cache generation's sampling domain without publishing GPU state.</summary>
internal sealed class SurfaceLightingParamsUbo : CpuUniformBuffer
{
    #region Public API
    #region Layout
    internal const int AtlasLayoutOffset = 0;
    internal const int SamplingOffset = 16;
    internal const int OriginOffset = 32;
    internal const int DimensionsOffset = 48;
    internal const int RingOffset = 64;
    internal const int PolicyOffset = 80;
    internal const int EnvironmentOffset = 96;
    internal const int SolarOffset = 112;
    internal const int SunDirectionOffset = 128;
    #endregion

    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 144;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Creates an unavailable cache domain with the full shared block layout.</summary>
    public SurfaceLightingParamsUbo() : base()
    {
        // Lookup consumers sample existing radiance rather than executing its lighting producer.
        // Keep producer-only sampling, policy and atmosphere fields at their zero defaults, but
        // provide the complete linked block size even when those members are unused by lookup.
    }

    /// <summary>Replaces the complete domain, including unavailable snapshots.</summary>
    public void Set(SurfaceLightingSnapshot? snapshot)
    {
        var value = snapshot.GetValueOrDefault();
        WriteUIntVector4(AtlasLayoutOffset, (uint)value.TileSize, (uint)value.TilesPerAxis, (uint)value.TilesPerAtlas, 0);
        WriteIntVector4(OriginOffset, value.Origin.X, value.Origin.Y, value.Origin.Z, 0);
        WriteIntVector4(DimensionsOffset, value.Dimensions.X, value.Dimensions.Y, value.Dimensions.Z, 0);
        WriteIntVector4(RingOffset, value.Ring.X, value.Ring.Y, value.Ring.Z, 0);
    }

    /// <summary>Packs one complete lighting operation and its admitted illumination before publication.</summary>
    internal void SetDispatch(in SurfaceLightingSnapshot input, uint operation, uint texels, uint rays,
        uint steps, uint frame, bool emission, int worldHeight, int maxFrames, bool measured,
        int maxTraceDistance, bool captureHitRetries, Vector3 environment, Vector3 solar, Vector3 sun)
    {
        // Build the entire candidate before changing the retained block, preserving rejected-edit atomicity.
        Span<byte> bytes = stackalloc byte[BlockSize];
        UboPacking.WriteUVec4(bytes, AtlasLayoutOffset, (uint)input.TileSize, (uint)input.TilesPerAxis, (uint)input.TilesPerAtlas, operation);
        UboPacking.WriteUVec4(bytes, SamplingOffset, texels, rays, steps, frame);
        UboPacking.WriteIVec4(bytes, OriginOffset, input.Origin.X, input.Origin.Y, input.Origin.Z, Math.Clamp(maxTraceDistance, 1, 512));
        UboPacking.WriteIVec4(bytes, DimensionsOffset, input.Dimensions.X, input.Dimensions.Y, input.Dimensions.Z, captureHitRetries ? 1 : 0);
        UboPacking.WriteIVec4(bytes, RingOffset, input.Ring.X, input.Ring.Y, input.Ring.Z, 0);
        UboPacking.WriteUVec4(bytes, PolicyOffset, emission ? 1u : 0u, (uint)Math.Max(0, worldHeight), (uint)Math.Clamp(maxFrames, 1, 255), measured ? 1u : 0u);
        UboPacking.WriteVec4(bytes, EnvironmentOffset, environment.X, environment.Y, environment.Z, 0);
        UboPacking.WriteVec4(bytes, SolarOffset, solar.X, solar.Y, solar.Z, 0);
        UboPacking.WriteVec4(bytes, SunDirectionOffset, sun.X, sun.Y, sun.Z, 0);
        SetBytes(bytes);
    }
    #endregion
}
