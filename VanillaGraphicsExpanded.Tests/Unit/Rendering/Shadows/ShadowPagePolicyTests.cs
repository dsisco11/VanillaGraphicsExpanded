using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.Sampling;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Protects configurable page allowances and sample demand's depth-inclusive filter footprint.</summary>
public sealed class ShadowPagePolicyTests
{
    #region Public API
    /// <summary>Frozen policy sanitizes unsupported capacities and nonfinite/negative bias values.</summary>
    [Fact]
    public void PageSettingsSanitizeAndFreezeFiniteBoundedValues()
    {
        // Arrange: deliberately supply invalid dimensions, work ceilings, depth range and bias values.
        var settings = new ShadowPageSettings { MemoryMiB = 0, PageSize = 96, TileColumns = int.MaxValue,
            TileRows = -2, BankCapacity = 8, ProbeLimit = 0, OperationsPerBatch = -1, PointNear = double.NaN,
            DepthBiasTexels = double.PositiveInfinity, NormalBiasTexels = -1, SlopeBiasTexels = 99 };

        // Act: freeze the effective policy, then mutate the original settings object.
        var frozen = settings.Freeze();
        settings.PageSize = 512;

        // Assert: every effective value is finite, bounded, and independent of subsequent configuration edits.
        Assert.Equal(128, frozen.MemoryMiB);
        Assert.Equal(128, frozen.PageSize);
        Assert.Equal(16, frozen.TileColumns);
        Assert.Equal(16, frozen.TileRows);
        Assert.Equal(4, frozen.BankCapacity);
        Assert.Equal(128, frozen.ProbeLimit);
        Assert.Equal(256, frozen.OperationsPerBatch);
        Assert.Equal(0.05, frozen.PointNear);
        Assert.Equal(0.5, frozen.DepthBiasTexels);
        Assert.Equal(0.5, frozen.NormalBiasTexels);
        Assert.Equal(1, frozen.SlopeBiasTexels);
    }

    /// <summary>Valid bank-grid choices remain configurable in the frozen page allocation policy.</summary>
    [Fact]
    public void PageBankGridPersistsAsIndependentTileAndBankLimits()
    {
        // Arrange: choose a non-square tile grid and fewer banks than the four statically bound consumers.
        var settings = new ShadowPageSettings { TileColumns = 3, TileRows = 5, BankCapacity = 2 };

        // Act: freeze the same effective policy captured by a page pool.
        var frozen = settings.Freeze();

        // Assert: each user-configurable dimension survives without collapsing into an integer array-layer count.
        Assert.Equal(3, frozen.TileColumns);
        Assert.Equal(5, frozen.TileRows);
        Assert.Equal(2, frozen.BankCapacity);
    }

    /// <summary>Nonnegative finite depth bits retain numeric ordering for imageAtomicMin and clear to one.</summary>
    [Fact]
    public void AtomicDepthEncodingRoundTripsAndOrdersSupportedDepths()
    {
        // Arrange: include both endpoints and representative fractional depths from the normalized interval.
        float[] depths = [0, 0.125f, 0.5f, 0.875f, 1];

        // Act: encode unsigned storage words and decode them through the production CPU representation helper.
        uint[] encoded = depths.Select(ShadowDepthEncoding.Encode).ToArray();

        // Assert: unsigned integer ordering agrees with depth ordering and the empty-tile sentinel is exactly one.
        Assert.Equal(encoded.Order().ToArray(), encoded);
        Assert.Equal(ShadowDepthEncoding.Encode(1), ShadowDepthEncoding.Clear);
        Assert.Equal(depths, encoded.Select(ShadowDepthEncoding.Decode).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadowDepthEncoding.Encode(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadowDepthEncoding.Encode(-0.01f));
    }

    /// <summary>Directional and point sample builders include normal/filter support in receiving coverage.</summary>
    [Fact]
    public void SampleFootprintExpandsWithTexelScaleAndUsesCurrentProjection()
    {
        // Arrange: use independent celestial and point projections with one finite receiving point.
        var settings = new ShadowPageSettings { PageSize = 128, NormalBiasTexels = 0.5 };
        var source = new ShadowSourceKey(8, 0, ShadowSourceKind.Sun, 1, 2, 0);
        var bounds = new ShadowBounds(-100, -100, -100, 100, 100, 100);
        var sun = new DirectionalShadowProjection(source, 3, Vector3d.UnitZ, bounds, baseTexelWidth: 2, pageSize: 128);
        var receiver = new Vector3d(12, 20, 30);

        // Act: request fine/coarse sun levels and one radial point-light footprint at the same receiver.
        var fine = ShadowSampleRequest.Directional(sun, 0, receiver, Vector3d.UnitY, 4, 5, 6, settings);
        var coarse = ShadowSampleRequest.Directional(sun, 1, receiver, Vector3d.UnitY, 4, 5, 6, settings);
        var pointProjection = new PointShadowProjection(source with { Kind = ShadowSourceKind.Entity }, 7,
            Vector3d.Zero, far: 200, faceResolution: 128, pageSize: 128);
        var point = ShadowSampleRequest.Point(pointProjection, receiver, Vector3d.UnitY, 4, 5, 6, settings);

        // Assert: coverage contains the receiver and grows with projected texel width, including bias/filter support.
        Assert.True(fine.Footprint.Contains(new(receiver.X, receiver.Y, receiver.Z, receiver.X, receiver.Y, receiver.Z)));
        Assert.Equal(fine.Footprint.MaxX - receiver.X, 7);
        Assert.Equal(coarse.Footprint.MaxX - receiver.X, 14);
        double pointHalfExtent = (3 * (1 + 8.0 / 128) + settings.NormalBiasTexels)
            * (2 * Math.Sqrt(12 * 12 + 20 * 20 + 30 * 30) / 128);
        Assert.Equal(pointHalfExtent, point.Footprint.MaxX - receiver.X, 12);
        Assert.Equal(4, fine.PageRequest.WorldRevision);
        Assert.Equal(5, fine.PageRequest.SceneEpoch);
        Assert.Equal(6, fine.PageRequest.SceneSequence);
    }
    #endregion
}
