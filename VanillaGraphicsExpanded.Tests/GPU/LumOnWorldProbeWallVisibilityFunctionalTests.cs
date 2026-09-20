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

/// <summary>Characterizes false visibility rejection on a fully illuminated planar wall using the production debug shader.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnWorldProbeWallVisibilityFunctionalTests : LumOnShaderFunctionalTestBase
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
    /// <summary>Every geometric segment is clear, yet near-wall pixels alternate between accepted and rejected probes.</summary>
    [Theory]
    [InlineData(0.01f, true)]
    [InlineData(2f, false)]
    public void FlatIlluminatedWall_CharacterizesFalseOcclusion(float inset, bool expectDefect)
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

        var irradiance = RenderWallDiagnostic(atlas, sampleZ, 31);
        var confidence = RenderWallDiagnostic(atlas, sampleZ, 33);
        int rejected = 0;
        int accepted = 0;
        float expectedToneMapped = MathF.PI / (1 + MathF.PI);
        for (int pixel = 0; pixel < GridSize * GridSize; pixel++)
        {
            int i = pixel * 4;
            if (confidence[i] < 0.001f)
            {
                rejected++;
                for (int c = 0; c < 3; c++) Assert.Equal(0f, irradiance[i + c]);
            }
            else
            {
                accepted++;
                Assert.InRange(confidence[i], trace.Confidence - 0.002f, trace.Confidence + 0.002f);
                for (int c = 0; c < 3; c++)
                    Assert.InRange(irradiance[i + c], expectedToneMapped - 0.003f, expectedToneMapped + 0.003f);
            }
        }
        output.WriteLine($"Wall inset={inset}: {rejected}/{GridSize * GridSize} false rejections, {accepted} accepted; all exact segments clear.");
        Assert.True(accepted > 0, "The fixture must retain a lit control region.");
        // Characterization, not approval of the defect: the eventual repair should replace
        // this branch with rejected == 0 while retaining the same exact-ray ground truth.
        if (expectDefect) Assert.True(rejected > 0, "Expected current angular-depth mismatch to reproduce.");
        else Assert.Equal(0, rejected);
    }
    #endregion

    #region Production Diagnostic Rendering
    /// <summary>Renders the actual irradiance or confidence view over an orthographically reconstructed wall.</summary>
    private float[] RenderWallDiagnostic(WorldProbeAtlasData atlas, float sampleZ, int mode)
    {
        int program = CompileShaderWithDefines("lumon_debug.vsh", "lumon_debug_worldprobe.fsh", new()
        {
            ["VGE_LUMON_WORLDPROBE_ENABLED"] = "1",
            ["VGE_LUMON_WORLDPROBE_LEVELS"] = "1",
            ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = "1",
            ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = "32.0",
            ["VGE_LUMON_WORLDPROBE_OCTAHEDRAL_SIZE"] = "16",
            ["VGE_LUMON_WORLDPROBE_DIFFUSE_STRIDE"] = "2"
        });
        try
        {
            using var radiance = TestFramework.CreateTexture(atlas.Width, atlas.Height, PixelInternalFormat.Rgba16f, atlas.Radiance);
            using var visibility = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, atlas.Visibility);
            using var metadata = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rg32f, atlas.Metadata);
            using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new float[] { 0.5f });
            using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new float[] { 0.5f, 0.5f, 0, 1 });
            using var target = TestFramework.CreateTestGBuffer(GridSize, GridSize, PixelInternalFormat.Rgba16f);
            using var parameters = new ObjectParamsUbo("Tests.WallVisibility.Debug");
            var cpu = new LumOnDebugParamsUbo { DebugMode = mode };
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnDebugParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object);
            parameters.UploadAndBind(cpu.Bytes);
            // Column-major inverse projection maps pixel centers to x/y=[-5,5], z=sampleZ.
            float[] inverse = { 5,0,0,0, 0,5,0,0, 0,0,1,0, 0,0,sampleZ,1 };
            UpdateAndBindLumOnFrameUbo(program, invProjectionMatrix: inverse, screenWidth: GridSize, screenHeight: GridSize);
            UpdateAndBindLumOnWorldProbeUbo(program, new Vec3f(), Vector3.Zero,
                new[] { new Vector3(-15.5f) }, new[] { Vector3.Zero });
            radiance.Bind(0); visibility.Bind(1); metadata.Bind(2); depth.Bind(3); normal.Bind(4);
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "worldProbeRadianceAtlas"), 0);
            GL.Uniform1(GL.GetUniformLocation(program, "worldProbeVis0"), 1);
            GL.Uniform1(GL.GetUniformLocation(program, "worldProbeMeta0"), 2);
            GL.Uniform1(GL.GetUniformLocation(program, "primaryDepth"), 3);
            GL.Uniform1(GL.GetUniformLocation(program, "gBufferNormal"), 4);
            GL.UseProgram(0);
            TestFramework.RenderQuadTo(program, target);
            return target[0].ReadPixels();
        }
        finally { GL.DeleteProgram(program); }
    }
    #endregion
}
