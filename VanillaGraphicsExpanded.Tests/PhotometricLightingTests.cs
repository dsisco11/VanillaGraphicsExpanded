using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks source quantities, admitted storage and history bounds independently of rendering.</summary>
public sealed class PhotometricLightingTests
{
    #region Public API
    /// <summary>Changes to actual decoder inputs refresh calibration without mutating the engine tables.</summary>
    [Fact]
    public void ActiveDecoderTablesRefreshCalibrationAtomically()
    {
        float[] block = Enumerable.Range(0, 32).Select(i => .04f + i * .03f).ToArray();
        float[] sun = Enumerable.Range(0, 32).Select(i => .03f + i * .03f).ToArray();
        byte[] hues = [0, 0, 0, 0, 8], saturations = [0, 0, 0, 0, 128];
        float[] original = block.ToArray();
        var owner = new EngineLightingCalibration();
        LightingCalibration first = owner.Capture(block, sun, hues, saturations);
        Assert.Equal(first, owner.Capture(block, sun, hues, saturations));
        Assert.Equal(original, block);
        block[13] *= .8f;
        LightingCalibration second = owner.Capture(block, sun, hues, saturations);
        Assert.True(second.BlockReferenceLux > first.BlockReferenceLux);
        Assert.Equal(first.PointCandelaPerUnit, second.PointCandelaPerUnit);
        block[13] = float.NaN;
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.Capture(block, sun, hues, saturations));
        block[13] = original[13] * .8f;
        Assert.Equal(second, owner.Capture(block, sun, hues, saturations));
        float[] extendedSun = [.. sun, .99f];
        LightingCalibration extended = owner.Capture(block, extendedSun, hues, saturations);
        Assert.Equal(sun[31], extended.AccessorSkyFull);
        Assert.Equal((int)(sun[31] * 255.999f) / 255f, extended.TerrainSkyFull);
    }

    /// <summary>Active table calibration reproduces the nominated reference and rejects unusable source tables.</summary>
    [Fact]
    public void DecodedTorchFitsBlockAndPointReferences()
    {
        Vector3 block = new(145f / 255, 99f / 255, 71f / 255);
        Vector3 point = new(9, 6, 4);
        LightingCalibration calibration = LightingCalibration.FromDecodedTorch(block, point,
            10f / 255, 7f / 255, 1, .03f, 1);
        double peak = block.X;
        double blockY = (.2126 * block.X + .7152 * block.Y + .0722 * block.Z)
            * Math.Pow(peak - calibration.BlockNeutralFloor, 4)
            / (Math.Pow(peak - calibration.BlockNeutralFloor, 3) + .064) / peak * calibration.BlockReferenceLux;
        double pointY = (.2126 * point.X + .7152 * point.Y + .0722 * point.Z) * calibration.PointCandelaPerUnit;
        Assert.InRange(blockY, 3.501407, 3.501411);
        Assert.InRange(pointY, 3.501407, 3.501411);
        Assert.Throws<ArgumentException>(() => LightingCalibration.FromDecodedTorch(Vector3.Zero, point,
            10f / 255, 7f / 255, 1, .03f, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LightingCalibration.FromDecodedTorch(block, point,
            10f / 255, 1, 1, .03f, 1));
    }

    /// <summary>Source normalization preserves chromaticity while matching authored Rec.709 luminance.</summary>
    [Theory]
    [InlineData(1f, 1f, 1f, 100000f)]
    [InlineData(1f, .4f, .1f, 3.5014087f)]
    [InlineData(0f, 0f, 1f, 32f)]
    public void SourceIntensityMatchesLuminance(float red, float green, float blue, float intensity)
    {
        Vector3 source = LightingCalibration.SourceRgb(new(red, green, blue), intensity);
        double luminance = .2126 * source.X + .7152 * source.Y + .0722 * source.Z;
        Assert.InRange(luminance, intensity * .999999, intensity * 1.000001);
        if (red > 0) Assert.InRange(source.Y / source.X, green / red - .000001f, green / red + .000001f);
        Assert.InRange(LightingCalibration.IsotropicCandela(44), 3.501408f, 3.501410f);
    }

    /// <summary>Zero sources remain zero and malformed chromaticity or calibration cannot publish.</summary>
    [Fact]
    public void InvalidSourceAndCalibrationAreRejected()
    {
        Assert.Equal(Vector3.Zero, LightingCalibration.SourceRgb(Vector3.Zero, 0));
        Assert.Throws<ArgumentException>(() => LightingCalibration.SourceRgb(Vector3.Zero, 1));
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LightingCalibration.SourceRgb(Vector3.One, invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => LightingCalibration.IsotropicCandela(invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LightingCalibration(invalid, .01f).Validate());
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => LightingCalibration.SourceRgb(new(-1, 1, 1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightingCalibration(1, 1).Validate());
    }

    /// <summary>Fixed encoding preserves signed coefficients and rejects overflow before half-float publication.</summary>
    [Fact]
    public void PersistentStoragePreservesSignsAndRange()
    {
        foreach (float value in new[] { 0f, 32f, 131072f, LightingStorage.PersistentMaximum })
        {
            float stored = LightingStorage.EncodePersistent(value);
            Assert.Equal(value, LightingStorage.DecodePersistent(stored));
            Assert.True(float.IsFinite((float)(Half)stored));
            Assert.Equal(-stored, LightingStorage.EncodePersistent(-value, signed: true));
        }
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, MathF.BitIncrement(LightingStorage.PersistentMaximum) })
            Assert.Throws<ArgumentOutOfRangeException>(() => LightingStorage.EncodePersistent(invalid));
    }

    /// <summary>Metadata remains outward rounded while camera-driven storage preserves faint light independently of scene peaks.</summary>
    [Fact]
    public void FrameScaleFollowsCameraWhileTemporalConversionRetainsStrictLimits()
    {
        double exactBound = 945821327.847;
        float bound = LightingStorage.UpperBound(exactBound);
        Assert.True(bound >= exactBound);
        foreach (float camera in new[] { .001f, .0625f, 1f, 16f })
        {
            float scale = LightingStorage.FrameScale(camera, bound);
            Assert.Equal(camera, scale);
            Assert.True(scale >= LightingStorage.MinimumFrameScale);
        }
        Assert.Equal(.001f, LightingStorage.FrameScale(.001f, 0));
        Assert.Equal(LightingStorage.MinimumFrameScale, LightingStorage.FrameScale(float.Epsilon, float.MaxValue));
        Assert.Equal(1, LightingStorage.FrameScale(1, float.MaxValue));
        Assert.Equal(16f, LightingStorage.FrameScale(16, 0));
        Assert.Equal(8, LightingStorage.RescaleHistory(2, .5f, .125f, 65504));
        Assert.Throws<ArgumentOutOfRangeException>(() => LightingStorage.RescaleHistory(65504, 1, .0625f, 65504));
        Assert.Throws<ArgumentOutOfRangeException>(() => LightingStorage.FrameScale(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LightingStorage.FrameScale(float.MaxValue, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LightingStorage.RescaleHistory(0, float.Epsilon, float.MaxValue, 65504));
        foreach(float invalid in new[]{-1f,float.NaN,float.PositiveInfinity})
            Assert.Throws<ArgumentOutOfRangeException>(() => LightingStorage.FrameScale(1,invalid));
    }

    /// <summary>Originating component bounds survive independent current-source removal before frame summation.</summary>
    [Fact]
    public void HistoryUsesComponentMaximaRatherThanMaximumTotal()
    {
        var current = new FrameLightingBounds(100, 0, 0, 0, 0, 0);
        var history = new FrameLightingBounds(0, 200, 0, 0, 0, 0);
        Assert.Equal(300, current.IncludeHistory(history).Total);
        var originating = new FrameLightingHistory(7, .25f, history);
        Assert.Equal(300, originating.IncludeIn(current, 7).Total);
        Assert.Throws<InvalidOperationException>(() => originating.IncludeIn(current, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameLightingHistory(0, .25f, history).Validate());
        Assert.Equal(0, FrameLightingBounds.FromSources(0, 0, 0, 0, 0, 0, 0, 0, 0).Total);
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameLightingBounds.FromSources(-1, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    /// <summary>Existing frame publication owns the appended calibration and rejects partial invalid replacement.</summary>
    [Fact]
    public void FrameCalibrationIsPublishedAtomically()
    {
        using var frame = new VgeFrameUniformBuffer();
        Vector4 initial = MemoryMarshal.Read<Vector4>(frame.Bytes.Slice(544, 16));
        Assert.Equal(LightingCalibration.Default.BlockReferenceLux, initial.X);
        Assert.Equal(LightingCalibration.Default.BlockNeutralFloor, initial.Y);
        Assert.Equal(32, initial.Z);
        LightingCalibration initialCalibration = frame.LightingCalibration;
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.SetLightingCalibration(new(float.NaN, 0)));
        Assert.Equal(initial, MemoryMarshal.Read<Vector4>(frame.Bytes.Slice(544, 16)));
        Assert.Equal(initialCalibration, frame.LightingCalibration);
        frame.SetLightingCalibration(new(10, .1f));
        Assert.Equal(new LightingCalibration(10, .1f), frame.LightingCalibration);
        Assert.Equal(new Vector4(10, .1f, 32, LightingCalibration.EnginePointCandelaPerUnit),
            MemoryMarshal.Read<Vector4>(frame.Bytes.Slice(544, 16)));
    }

    /// <summary>Persistent generations ignore exposure while calibration and fixed encoding remain authoritative.</summary>
    [Fact]
    public void PersistentGenerationsAndAtmosphereEncodingPreserveQuantityBoundaries()
    {
        var generation = LightingStorageGeneration.Persistent(7);
        Assert.True(generation.CompatibleWith(LightingStorageGeneration.Persistent(7)));
        Assert.False(generation.CompatibleWith(LightingStorageGeneration.Persistent(8)));
        Assert.False(generation.CompatibleWith(new(7, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => LightingStorageGeneration.Persistent(0));
        float[] packed = new float[8];
        AtmosphereMieTransport.Pack([32, 64, 128, .25f], [], packed, 1, 1, 1, Vector3.UnitY, 0,
            LightingStorage.PersistentScale);
        Assert.Equal(new float[] { 2, 4, 8, 1, 0, 0, 0, 1 }, packed);
        AtmosphereMieTransport.Pack([32, 64, 128, .25f], [], packed, 1, 1, 1, Vector3.UnitY, 0);
        Assert.Equal(new float[] { 32, 64, 128, 1, 0, 0, 0, 1 }, packed);
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereMieTransport.Pack(
            [1048065, 0, 0, 1], [], packed, 1, 1, 1, Vector3.UnitY, 0, LightingStorage.PersistentScale));
    }

    /// <summary>Water source metadata covers the complete published illumination and rejects unsupported decoders.</summary>
    [Fact]
    public void WaterBoundsCoverSolarAndIsotropicSources()
    {
        Vector3 bound = WaterSourceBounds.FromIncident(new(100000, 0, 0), new(0, 40, 0));
        Assert.True(bound.X >= 6400000); Assert.True(bound.Y >= 40 / (4 * Math.PI));
        Assert.Equal(0, bound.Z);
        float[] levels = Enumerable.Range(0, 32).Select(x => x / 31f).ToArray();
        Assert.Equal(1, WaterSourceBounds.EngineFieldMaximum(levels));
        levels[0] = float.NaN;
        Assert.Throws<ArgumentOutOfRangeException>(() => WaterSourceBounds.EngineFieldMaximum(levels));
        Assert.Throws<ArgumentOutOfRangeException>(() => WaterSourceBounds.FromIncident(new(-1, 0, 0), Vector3.Zero));
    }
    #endregion
}
