using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.Water;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Unit.Water;

/// <summary>Checks managed liquid frame input snapshots and their diagnostic capture cost.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LiquidShaderProgramTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Explicitly measures repeated capture at each authored array load.</summary>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(0, 4, 1)]
    [InlineData(3, 4, 1)]
    [InlineData(0, 100, 3)]
    [InlineData(3, 100, 3)]
    [Trait("Purpose", "Diagnostic")]
    public void MeasureFrameCaptureAcrossArrayLoads(int mode,int lights,int spheres)
    {
        // Arrange diagnostic capture at the requested authored array load.
        recordTimings=true;
        // Act and assert through the shared capture contract exercised by the correctness cases.
        try { FrameCaptureRetainsCommonInputsAcrossArrayLoads(mode,lights,spheres); }
        finally { recordTimings=false; }
    }
    /// <summary>Captured frame inputs retain the selected light arrays, water counters and shared camera.</summary>
    [Theory]
    [InlineData(0, 100, 3)]
    [InlineData(3, 100, 3)]
    public void FrameCaptureRetainsCommonInputsAcrossArrayLoads(int mode, int lights, int spheres)
    {
        // Arrange controlled engine uniforms, adapter responses and borrowed frame resources.
        EnsureContextValid();
        using var publication = new TestFrameLightingPublication();
        using var platform = new EngineShaderPlatformScope();
        var uniforms = new DefaultShaderUniforms
        {
            PointLightsCount = lights,
            FogSphereQuantity = spheres,
            ColorMapRects4 = Enumerable.Range(0, 160).Select(value => (float)value).ToArray(),
            ZNear = .1f,
            ZFar = 100,
            WaterStillCounter = 2,
            WaterFlowCounter = 3,
            WindWaveCounter = 4
        };
        float[] projection = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        var api = LiquidFrameCaptureApi.Create(uniforms, projection);
        var program = new LiquidShaderProgram { CaptureMode = mode };
        using var sharedCamera = TestFrameCamera.CreateFromProjection(projection, 512, 512, .1f, 100);
        using var sharedLights = new VgeLightsUniformBuffer();
        program.LightsInputs = sharedLights;
        program.FrameInputs = sharedCamera;
        var tile = new Vec2f(.125f, .125f);
        // Act by capturing multiple frames and optionally sampling diagnostic throughput.
        // Two captures prove retention; warmed throughput sampling belongs to the explicit diagnostic.
        for (int frame = 0; frame < (recordTimings?8192:2); frame++) program.CaptureFrameInputs(api, tile);
        const int iterations = 2048;
        for (int sample = 0; sample < (recordTimings?5:0); sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread(), start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int frame = 0; frame < iterations; frame++) program.CaptureFrameInputs(api, tile);
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine($"frame-capture mode={mode} lights={lights} spheres={spheres} sample={sample} iterations={iterations} bytes={bytes} elapsedMs={elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency:R}");
        }
        // Assert the managed snapshot values and shared camera reference remain published.
        var frameBytes = ((ILiquidShaderProgramBindings)program).FrameParameters.Bytes;
        Assert.Equal(new Vector4(2, 3, 0, 4), MemoryMarshal.Read<Vector4>(frameBytes.Slice(128, 16)));
        Assert.Equal(new Vector4(156, 157, 158, 159), MemoryMarshal.Read<Vector4>(frameBytes.Slice(944, 16)));
        Assert.Same(sharedCamera, ((ILiquidShaderProgramBindings)program).FrameInputs);
        Assert.Equal(Matrix4x4.Identity, MemoryMarshal.Read<Matrix4x4>(sharedCamera.Bytes.Slice(128, 64)));
    }
    #endregion

    #region Private
    private bool recordTimings;

    #endregion
}
