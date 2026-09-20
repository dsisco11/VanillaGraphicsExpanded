using System.Numerics;
using System.Threading;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies visibility without false rejection on a fully illuminated planar wall using the production debug shader.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnWorldProbeWallVisibilityFunctionalTests : DirectWorldProbeVisibilityTestBase
{
    private const int GridSize = 128;
    private readonly ITestOutputHelper output;

    #region Construction
    /// <summary>Uses the shared GPU context and records reproducible failure counts.</summary>
    public LumOnWorldProbeWallVisibilityFunctionalTests(HeadlessGLFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        this.output = output;
    }
    #endregion

    #region Planar Surface Reproduction
    /// <summary>Every geometric segment is clear, and both near-wall and inset samples must retain irradiance.</summary>
    [Theory]
    [InlineData(0.01f, 31)]
    [InlineData(0.01f, -1)]
    [InlineData(0.01f, -2)]
    [InlineData(2f, 31)]
    public void FlatIlluminatedWall_PreservesAllVisibleSamples(float inset, int consumer)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld { DefaultLight = new Vector4(1, 1, 1, 0) };
        world.AddRoom((-8, -8, -8), (8, 8, 8));
        var probePosition = new Vector3d(0.5, 0.5, 0.5);
        var trace = WorldProbeRoomScenario.Trace(world, probePosition);
        Assert.True(trace.Success);
        Assert.Equal(256, trace.AtlasSamples.Length);
        Assert.All(trace.AtlasSamples, sample =>
        {
            Assert.True(sample.AlphaEncodedDistSigned > 0);
            Assert.InRange(sample.RadianceRgb.X, 0.999f, 1.001f);
            Assert.InRange(sample.RadianceRgb.Y, 0.999f, 1.001f);
            Assert.InRange(sample.RadianceRgb.Z, 0.999f, 1.001f);
        });
        var atlas = new WorldProbeAtlasData(1, WorldProbeRoomScenario.TileSize);
        atlas.SetProbe(trace);

        // A dense square strictly inside the flat z=8 wall. Trace exact directions to
        // establish ground truth independently of the GPU's quantized direction lookup.
        float sampleZ = 8 - inset;
        var scene = world.CreateTraceScene();
        for (int y = 0; y < GridSize; y++)
        for (int x = 0; x < GridSize; x++)
        {
            var point = new Vector3d(((x + 0.5) / GridSize * 2 - 1) * 5,
                ((y + 0.5) / GridSize * 2 - 1) * 5, sampleZ);
            var delta = point - probePosition;
            var direction = new Vector3((float)delta.X, (float)delta.Y, (float)delta.Z);
            var outcome = scene.Trace(probePosition, Vector3.Normalize(direction), 64, CancellationToken.None, out var hit);
            Assert.Equal(WorldProbeTraceOutcome.Hit, outcome);
            Assert.Equal(8, hit.HitBlockPos.Z);
            Assert.True(hit.HitDistance > delta.Length(), "The probe-to-sample segment must be unobstructed.");
        }

        using var local = new LocalTraceVoxelFixture();
        local.Publish(world);
        var irradiance = RenderDirectVisibility(atlas, local.Scene, new Vector3(0, 0, sampleZ), new Vector3(-15.5f), 32, consumer, GridSize, 5);
        var confidence = consumer >= 0
            ? RenderDirectVisibility(atlas, local.Scene, new Vector3(0, 0, sampleZ), new Vector3(-15.5f), 32, 33, GridSize, 5)
            : irradiance;
        int rejected = 0;
        int accepted = 0;
        float expectedLighting = consumer >= 0 ? MathF.PI / (1 + MathF.PI) : MathF.PI;
        for (int pixel = 0; pixel < GridSize * GridSize; pixel++)
        {
            int i = pixel * 4;
            float sampleConfidence = confidence[i + (consumer >= 0 ? 0 : 3)];
            if (sampleConfidence < 0.001f)
            {
                rejected++;
                for (int c = 0; c < 3; c++) Assert.Equal(0f, irradiance[i + c]);
            }
            else
            {
                accepted++;
                Assert.InRange(sampleConfidence, trace.Confidence - 0.002f, trace.Confidence + 0.002f);
                for (int c = 0; c < 3; c++)
                    Assert.InRange(irradiance[i + c], expectedLighting - 0.003f, expectedLighting + 0.003f);
            }
        }
        output.WriteLine($"Consumer={consumer}, wall inset={inset}: {rejected}/{GridSize * GridSize} false rejections, {accepted} accepted; all exact segments clear.");
        Assert.True(accepted > 0, "The fixture must retain a lit control region.");
        Assert.Equal(0, rejected);
    }
    #endregion

}
