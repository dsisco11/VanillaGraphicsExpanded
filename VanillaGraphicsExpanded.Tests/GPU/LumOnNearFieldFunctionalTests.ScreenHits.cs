using System.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares screen-resolved and voxel-resolved lighting for the same non-emissive room.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Screen Hit Lighting
    /// <summary>Adding visible geometry must not erase outgoing radiance supplied by the same near-field scene off screen.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    [InlineData(1f)]
    public void ScreenHits_PreserveNonEmissiveLocalLighting(float lighting)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        world.FillLight((-2, -2, -7), (2, 2, -3), new Vector4(lighting, lighting, lighting, 0));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world);
        float depth = ScreenDepthAt(-7); // Interior face of the back wall occupies z=-8..-7.
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false);
        var offScreen = Trace(fixture);
        var onScreen = Trace(fixture, screenDepth: depth);
        var suppressed = Trace(fixture, screenDepth: depth, suppress: true);
        Assert.Equal(onScreen.Radiance, suppressed.Radiance);
        Assert.Equal(onScreen.Meta, suppressed.Meta);
        int checkedHits = 0;
        for (int pixel = 0; pixel < onScreen.Meta.Length / 2; pixel++)
        {
            // The independent screen-only control proves these directions actually
            // take the accepted screen-hit branch, avoiding a vacuous miss-path test.
            if ((Flags(screenOnly.Meta[pixel * 2 + 1]) & 1u) == 0) continue;
            checkedHits++;
            Assert.Equal(1f, onScreen.Meta[pixel * 2]);
            Assert.Equal(1u, Flags(onScreen.Meta[pixel * 2 + 1]) & 1u);
            Assert.Equal(0u, Flags(onScreen.Meta[pixel * 2 + 1]) & (1u << 5));
            for (int c = 0; c < 3; c++)
            {
                Assert.InRange(offScreen.Radiance[pixel * 4 + c], lighting - 0.002f, lighting + 0.002f);
                Assert.InRange(onScreen.Radiance[pixel * 4 + c],
                    offScreen.Radiance[pixel * 4 + c] - 0.002f, offScreen.Radiance[pixel * 4 + c] + 0.002f);
            }
        }
        Assert.True(checkedHits > 0, "The visible plane must produce accepted screen hits.");
    }

    /// <summary>A geometric screen hit without a ready local lighting source must not publish confident darkness.</summary>
    [Fact]
    public void ScreenHits_UnavailableLighting_RemainsUnresolved()
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture();
        float depth = ScreenDepthAt(-7);
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false);
        var result = Trace(fixture, screenDepth: depth);
        int checkedHits = 0;
        for (int pixel = 0; pixel < result.Meta.Length / 2; pixel++)
        {
            if ((Flags(screenOnly.Meta[pixel * 2 + 1]) & 1u) == 0) continue;
            checkedHits++;
            Assert.Equal(0f, result.Meta[pixel * 2]);
            for (int c = 0; c < 3; c++) Assert.Equal(0f, result.Radiance[pixel * 4 + c]);
        }
        Assert.True(checkedHits > 0);
    }

    /// <summary>Shared near-field shading counts emission once; unavailable local data preserves explicit visible emission.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ScreenHits_EmissionIsNotDoubled_AndSurvivesUnavailableLocalData(bool publish)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        if (publish) fixture.Publish(world, new Vector4(1, 1, 1, 0.25f));
        float depth = ScreenDepthAt(-7);
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false, screenEmission: 0.25f, emissionBoost: 2);
        var result = Trace(fixture, screenDepth: depth, screenEmission: 0.25f, emissionBoost: 2);
        var suppressed = Trace(fixture, screenDepth: depth, screenEmission: 0.25f, emissionBoost: 2, suppress: true);
        Assert.Equal(result.Radiance, suppressed.Radiance);
        Assert.Equal(result.Meta, suppressed.Meta);
        int checkedHits = 0;
        for (int pixel = 0; pixel < result.Meta.Length / 2; pixel++)
        {
            if ((Flags(screenOnly.Meta[pixel * 2 + 1]) & 1u) == 0) continue;
            checkedHits++;
            Assert.Equal(1f, result.Meta[pixel * 2]);
            // RGBA8 emission quantization permits roughly 0.502 rather than exactly 0.5.
            for (int c = 0; c < 3; c++) Assert.InRange(result.Radiance[pixel * 4 + c], 0.495f, 0.505f);
        }
        Assert.True(checkedHits > 0);
    }

    /// <summary>A nearer known opaque voxel cannot borrow emission from a screen hit when its own material is unavailable.</summary>
    [Fact]
    public void ScreenHits_MissingLocalMaterial_DoesNotBorrowScreenEmission()
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(world, materialIdentity: 0);
        float depth = ScreenDepthAt(-7);
        var screenOnly = Trace(fixture, screenDepth: depth, nearFieldTracing: false, screenEmission: 1);
        var result = Trace(fixture, screenDepth: depth, screenEmission: 1);
        int checkedHits = 0;
        for (int pixel = 0; pixel < result.Meta.Length / 2; pixel++)
        {
            if ((Flags(screenOnly.Meta[pixel * 2 + 1]) & 1u) == 0) continue;
            checkedHits++;
            Assert.Equal(0f, result.Meta[pixel * 2]);
            for (int c = 0; c < 3; c++) Assert.Equal(0f, result.Radiance[pixel * 4 + c]);
        }
        Assert.True(checkedHits > 0);
    }

    /// <summary>Projects a constant view-space plane using the same projection as the trace harness.</summary>
    private static float ScreenDepthAt(float z)
    {
        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        return (projection[10] * z + projection[14]) / (projection[11] * z + projection[15]) * 0.5f + 0.5f;
    }
    #endregion
}
