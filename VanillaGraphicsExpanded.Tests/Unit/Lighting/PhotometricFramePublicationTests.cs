using System.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Lighting;

/// <summary>Checks assembled physical envelopes against matching published sources and receiver modes.</summary>
public sealed class PhotometricFramePublicationTests
{
    #region Public API
    /// <summary>A visible lunar disk contributes its finite-source radiance bound even when the solar raster is absent.</summary>
    [Fact]
    public void NightEnvelopeIncludesFiniteLunarResponse()
    {
        // Arrange: publish a night snapshot with a finite lunar disk and prepare a no-moon comparison.
        using var lights = new VgeLightsUniformBuffer();
        var night = Snapshot() with { Sun = -Vector3.UnitY, Solar = Vector3.Zero, Moon = Vector3.UnitY,
            HasNightSources = true, Lunar = new Vector3(.1f), MoonAngularRadius = .03252f };
        // Act: assemble the dark and lunar source bounds from the same publication inputs.
        var dark = FrameLightingBounds.FromPublications(night with { Lunar = Vector3.Zero }, lights,
            LightingCalibration.Default, false, 1, Vector3.One, 0, 0);
        var moon = FrameLightingBounds.FromPublications(night, lights, LightingCalibration.Default, false, 1, Vector3.One, 0, 0);
        double equivalent = .1f / (MathF.PI * MathF.Pow(MathF.Sin(night.MoonAngularRadius), 2));
        double guard = 1 + 256.0 / (1 << 23);
        // Assert: lunar response contributes its finite-source bound and rejects an unsupported angular radius.
        Assert.InRange(Math.Abs(moon.WaterReflection - dark.WaterReflection - 3 * equivalent * guard), 0, .0001);
        Assert.True(moon.Surface > dark.Surface);
        Assert.Throws<ArgumentException>(() => FrameLightingBounds.FromPublications(night with { MoonAngularRadius = .001f },
            lights, LightingCalibration.Default, false, 1, Vector3.One, 0, 0));
    }

    /// <summary>Concurrent sun and moon bounds preserve both lux contributions and equivalent finite-disk radiance.</summary>
    [Fact]
    public void ConcurrentCelestialSourcesRetainSummedIlluminanceAndDiskRadiance()
    {
        // Arrange both emitters as visible red sources so sum, max, or selected-emitter policies produce distinct bounds.
        using var lights = new VgeLightsUniformBuffer();
        var solar = Snapshot() with
        {
            Sun = Vector3.UnitY, Solar = new(100, 0, 0), Moon = Vector3.UnitY, Lunar = Vector3.Zero,
            Environment = Vector3.Zero, HorizonElevation = 0, HasNightSources = true
        };
        var lunar = solar with { Solar = Vector3.Zero, Lunar = new(40, 0, 0) };
        var both = solar with { Lunar = new(40, 0, 0) };
        var calibration = new LightingCalibration(120, .1f);

        // Act: assemble separate source bounds and the concurrent publication under the same calibrated block reference.
        var sunBounds = FrameLightingBounds.FromPublications(solar, lights, calibration, false, 1, Vector3.One, 0, 0);
        var moonBounds = FrameLightingBounds.FromPublications(lunar, lights, calibration, false, 1, Vector3.One, 0, 0);
        var combinedBounds = FrameLightingBounds.FromPublications(both, lights, calibration, false, 1, Vector3.One, 0, 0);
        double guard = 1 + 256.0 / (1 << 23);
        double blockDiffuse = calibration.BlockReferenceLux * (1 - calibration.BlockNeutralFloor) / (4 * Math.PI) * guard;
        double expectedDirectScattering = (2 / .0722) * (64 * (100 + 40) * guard + blockDiffuse);
        double solarDisk = 100 / (Math.PI * Math.Pow(Math.Sin(AtmosphereSolarDisk.AngularRadius), 2));
        double lunarDisk = 40 / (Math.PI * Math.Pow(Math.Sin(both.MoonAngularRadius), 2));
        double expectedReflection = 3 * (solarDisk + lunarDisk) * guard;

        // Assert: lux remains independent of block calibration, disk response adds both finite sources,
        // and compatibility ambient follows Environment + (Solar + Lunar) / pi in the documented display mapping.
        // The analytical disk oracle uses double precision while production radiance and source RGB are Float32;
        // this relative tolerance covers their rounding and remains far below the omitted lunar contribution.
        Assert.InRange(Math.Abs(combinedBounds.SurfaceScattering - expectedDirectScattering), 0, .0001);
        Assert.True(combinedBounds.SurfaceScattering > Math.Max(sunBounds.SurfaceScattering, moonBounds.SurfaceScattering));
        Assert.InRange(Math.Abs(combinedBounds.WaterReflection - expectedReflection), 0, expectedReflection * 2e-6);
        Assert.True(combinedBounds.WaterReflection > Math.Max(sunBounds.WaterReflection, moonBounds.WaterReflection));
        double ambientLinear = (100 + 40) / Math.PI;
        double ambientMapped = ambientLinear / (1 + ambientLinear);
        double expectedAmbientRed = 1.055 * Math.Pow(ambientMapped, 1 / 2.4) - .055;
        Assert.InRange(Math.Abs(AtmosphereLegacyLighting.From(both).Ambient.X - expectedAmbientRed), 0, .000001);
    }
    /// <summary>Indirect mode, gain and tint enter once, while source peaks and both decoder endpoints remain explicit.</summary>
    [Theory]
    [InlineData(false, 0f)] [InlineData(false, 2f)]
    [InlineData(true, 0f)] [InlineData(true, 2f)]
    public void PublishedEnvelopeUsesComponentPeaksAndActiveReceiverMode(bool lumon, float gain)
    {
        // Arrange: publish distinct source channels and receiver settings for one mode/gain combination.
        using var lights = new VgeLightsUniformBuffer();
        lights.CaptureAuthored([new(Vector3.Zero, new(1, .25f, .1f), 44, 10)]);
        var atmosphere = Snapshot();
        var calibration = new LightingCalibration(12, .25f,
            AccessorBlockReferenceLux: 120, AccessorBlockNeutralFloor: .1f);
        var tint = new Vector3(2, 7, 3);
        // Act: assemble the envelope and a paired terrain-reference variation.
        var actual = FrameLightingBounds.FromPublications(atmosphere, lights, calibration, lumon, gain, tint, 5, 9);
        double guard = 1 + 256.0 / (1 << 23);
        double block = 120 * (1.0 - .1f);
        double indirect = lumon ? Math.Max(block / Math.PI,
            (1.28 * LightingStorage.DirectionalMaximum + 9.36161e-7) * gain * 7) : 11 + block / Math.PI;
        double disk = AtmosphereSolarDisk.Radiance(atmosphere).Z;
        double expectedSurface = 3 * disk * guard + (40000000 + 1 / Math.PI) * lights.SummedCandelaMaximum
            + 2 * 17 * guard / Math.PI + indirect * guard + 5 * guard;
        // Assert: source peaks, active receiver mode, and terrain reference contribute once at their published bounds.
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
        // Arrange: alter one publication identity/quantity or provide an invalid receiver gain.
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
        // Act and assert: unsupported publication inputs cannot produce an admitted scale envelope.
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
