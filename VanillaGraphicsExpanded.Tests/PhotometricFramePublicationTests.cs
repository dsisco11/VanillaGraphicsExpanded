using System.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks assembled physical envelopes against matching published sources and receiver modes.</summary>
public sealed class PhotometricFramePublicationTests
{
    #region Public API
    /// <summary>A visible lunar disk contributes its finite-source radiance bound even when the solar raster is absent.</summary>
    [Fact]
    public void NightEnvelopeIncludesFiniteLunarResponse()
    {
        using var lights = new VgeLightsUniformBuffer();
        var night = Snapshot() with { Sun = -Vector3.UnitY, Solar = Vector3.Zero, Moon = Vector3.UnitY,
            HasNightSources = true, Lunar = new Vector3(.1f), MoonAngularRadius = .03252f };
        var dark = FrameLightingBounds.FromPublications(night with { Lunar = Vector3.Zero }, lights,
            LightingCalibration.Default, false, 1, Vector3.One, 0, 0);
        var moon = FrameLightingBounds.FromPublications(night, lights, LightingCalibration.Default, false, 1, Vector3.One, 0, 0);
        double equivalent = .1f / (MathF.PI * MathF.Pow(MathF.Sin(night.MoonAngularRadius), 2));
        double guard = 1 + 256.0 / (1 << 23);
        Assert.InRange(Math.Abs(moon.WaterReflection - dark.WaterReflection - 3 * equivalent * guard), 0, .0001);
        Assert.True(moon.Surface > dark.Surface);
        Assert.Throws<ArgumentException>(() => FrameLightingBounds.FromPublications(night with { MoonAngularRadius = .001f },
            lights, LightingCalibration.Default, false, 1, Vector3.One, 0, 0));
    }
    /// <summary>Indirect mode, gain and tint enter once, while source peaks and both decoder endpoints remain explicit.</summary>
    [Theory]
    [InlineData(false, 0f)] [InlineData(false, 2f)]
    [InlineData(true, 0f)] [InlineData(true, 2f)]
    public void PublishedEnvelopeUsesComponentPeaksAndActiveReceiverMode(bool lumon, float gain)
    {
        using var lights = new VgeLightsUniformBuffer();
        lights.CaptureAuthored([new(Vector3.Zero, new(1, .25f, .1f), 44, 10)]);
        var atmosphere = Snapshot();
        var calibration = new LightingCalibration(12, .25f,
            AccessorBlockReferenceLux: 120, AccessorBlockNeutralFloor: .1f);
        var tint = new Vector3(2, 7, 3);
        var actual = FrameLightingBounds.FromPublications(atmosphere, lights, calibration, lumon, gain, tint, 5, 9);
        double guard = 1 + 256.0 / (1 << 23);
        double block = 120 * (1.0 - .1f);
        double indirect = lumon ? Math.Max(block / Math.PI,
            (1.28 * LightingStorage.DirectionalMaximum + 9.36161e-7) * gain * 7) : 11 + block / Math.PI;
        double disk = AtmosphereSolarDisk.Radiance(atmosphere).Z;
        double expectedSurface = 3 * disk * guard + (40000000 + 1 / Math.PI) * lights.SummedCandelaMaximum
            + 2 * 17 * guard / Math.PI + indirect * guard + 5 * guard;
        Assert.InRange(Math.Abs(actual.Surface - expectedSurface), 0, .000001);
        Assert.Equal(132, actual.AirSky);
        Assert.Equal(9, actual.Additive);
        var brighterTerrain = FrameLightingBounds.FromPublications(atmosphere, lights,
            calibration with { BlockReferenceLux = 400 }, lumon, 0, tint, 5, 9);
        var baselineFallback = FrameLightingBounds.FromPublications(atmosphere, lights, calibration, lumon, 0, tint, 5, 9);
        Assert.InRange(Math.Abs(brighterTerrain.Surface - baselineFallback.Surface - (300 - block) * guard / Math.PI), 0, .000001);
    }

    /// <summary>Unadmitted snapshots and unsupported source quantities cannot produce a usable scale envelope.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void AssembledEnvelopeRejectsUnsupportedPublication(int invalidKind)
    {
        using var lights = new VgeLightsUniformBuffer();
        var atmosphere = Snapshot();
        atmosphere = invalidKind switch
        {
            0 => atmosphere with { CalibrationGeneration = 0 },
            1 => atmosphere with { LookupStorageScale = 1 },
            2 => atmosphere with { Solar = new Vector3(float.NaN) },
            3 => atmosphere with { Environment = new Vector3(float.PositiveInfinity) },
            _ => atmosphere
        };
        Assert.ThrowsAny<ArgumentException>(() => FrameLightingBounds.FromPublications(atmosphere, lights,
            LightingCalibration.Default, true, invalidKind == 4 ? float.NaN : 1, Vector3.One, 0, 0));
    }
    #endregion

    #region Private
    /// <summary>Supplies already admitted lookup metadata with distinct dominant RGB channels.</summary>
    private static AtmosphereLighting Snapshot() => new(Vector3.UnitY, new(3, 7, 17), new(11, 5, 2),
        Vector3.Zero, Vector3.Zero, [])
    { CalibrationGeneration = 7, LookupStorageScale = LightingStorage.PersistentScale, ReconstructionMaximumNits = 44 };
    #endregion
}
