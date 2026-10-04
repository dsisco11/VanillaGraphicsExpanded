using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Liquids;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Records exact production traversal decisions on controlled opaque receiver fields.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionDiagnosticTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Enumerates existing optical reproductions across every ray budget and background resolution.</summary>
    public static IEnumerable<object[]> ReceiverCases()
    {
        string[] labels = ["flat","tilted","shallow-plane","adjacent-foreground","sky","range-exhaustion",
            "edge-motion","range-fade","positive-view-z","metadata","depth-discontinuity","grazing"];
        foreach (int budget in new[] {2,4,8})
        foreach (bool half in new[] {false,true})
        for (int scenario = 0; scenario < labels.Length; scenario++)
            yield return [scenario,labels[scenario],budget,half];
    }

    /// <summary>Separates unavailable geometric support from fully weighted accepted receivers.</summary>
    [Theory]
    [MemberData(nameof(ReceiverCases))]
    public void BaselineReceiverDiagnostics(int scenario, string label, int budget, bool half)
    {
        EnsureContextValid();
        const int size = 128;
        float receiverDepth = scenario switch { 2 => 2.01f, 5 => 80f, 7 => 30f, 8 => 1.7f, _ => 10f };
        float deviceDepth = DeviceDepth(receiverDepth);
        var depths = Enumerable.Repeat(scenario == 4 ? 1f : deviceDepth, size * size).ToArray();
        if (scenario == 3) depths[64 * size + 65] = DeviceDepth(1);
        // An oblique submerged plane remains eligible while the bent ray moves toward the camera.
        if (scenario == 8)
            for (int y = 0; y < size; ++y)
                for (int x = 0; x < size; ++x)
                {
                    float rayX = (2f * (x + .5f) / size - 1) / .1f;
                    float distance = -6.572f / (-.7f * rayX + .714f);
                    depths[y * size + x] = distance > .1f ? DeviceDepth(distance) : 1f;
                }
        if (scenario == 10)
            for (int y = 0; y < size; ++y)
                for (int x = 0; x < size; ++x)
                    depths[y * size + x] = DeviceDepth(x < 69 ? 20 : 3);
        using var depth = DynamicTexture2D.Create(size, size, PixelInternalFormat.R32f);
        using var color = DynamicTexture2D.Create(size, size, PixelInternalFormat.Rgba32f);
        var colors = Enumerable.Range(0, size * size).SelectMany(_ => new[] { 4f, 2f, 1f, scenario == 9 ? 0f : 1f }).ToArray();
        WaterReceiverTestInputs.EncodeDepthValidity(colors, depths);
        depth.UploadDataImmediate(depths);
        color.UploadDataImmediate(colors);
        var program = Programs.Create<WaterRefractionDiagnosticShaderProgram>();
        var inputs = (IWaterRefractionDiagnosticBindings)program;
        inputs.Scenario = scenario;
        float focal = scenario == 8 ? .1f : MathF.Sqrt(3);
        var projection = new Matrix4x4(focal,0,0,0, 0,focal,0,0, 0,0,-100.1f/99.9f,-1, 0,0,-20f/99.9f,0);
        Assert.True(Matrix4x4.Invert(projection, out var inverse));
        inputs.Projection = projection; inputs.InverseProjection = inverse;
        inputs.Budget = budget;
        inputs.FrameSize = new(size, size);
        inputs.Color = color;
        inputs.Depth = depth;
        using var halfColor = half ? DynamicTexture2D.Create(size / 2,size / 2,PixelInternalFormat.Rgba32f) : null;
        using var halfDepth = half ? DynamicTexture2D.Create(size / 2,size / 2,PixelInternalFormat.Rgba32f) : null;
        using var reduced = half ? GpuFramebuffer.CreateMRT([halfColor!,halfDepth!]) : null;
        if (half)
        {
            var reduction = Programs.Create<WaterRefractionReductionShaderProgram>();
            reduction.SourceColor = color; reduction.SourceDepth = depth;
            TestFramework.RenderQuadTo(reduction,reduced!);
            inputs.Color = halfColor!; inputs.Depth = halfDepth!;
        }
        using var target = CreateMRTRenderTarget(size, size, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        int[] columns = scenario == 6 ? [1, 3, 6, 12, 32, 64] : [64];
        foreach (int x in columns)
        {
            float[] decision = target[0].ReadPixelsRegion(x, 64, 1, 1);
            float[] sampled = target[1].ReadPixelsRegion(x, 64, 1, 1);
            float[] transport = target[2].ReadPixelsRegion(x, 64, 1, 1);
            float[] work = target[6].ReadPixelsRegion(x, 64, 1, 1);
            // Published depth alone supplies search eligibility. Only a final
            // accepted triangle may fetch its three associated radiance samples.
            Assert.Equal(0, work[2]);
            Assert.Equal(decision[0], work[3]);
            Assert.Equal(decision[0] * 3, work[1]);
            Assert.InRange(work[1], 0, work[0]);
            Assert.InRange(work[0], 0, decision[2] * 6);
            if (scenario == 4) Assert.Equal(0, work[1]);
            output.WriteLine($"{label} x={x}: hit/reason/evaluations/confidence=[{string.Join(",", decision)}]; uv/depth=[{string.Join(",", sampled)}]; length/directionZ/fallback/receiverZ=[{string.Join(",", transport)}]");
            Assert.All(decision.Concat(sampled).Concat(transport), value => Assert.True(float.IsFinite(value)));
            Assert.InRange(decision[2], 0, budget);
            if (decision[0] == 1) Assert.Equal(1,decision[3]);
            if (scenario is 0 or 1 or 2 or 7) Assert.Equal(1, decision[0]);
            if (scenario == 3) { Assert.Equal(1, decision[0]); Assert.Equal(0, decision[1]); }
            if (scenario is 4 or 9) Assert.Equal(2, decision[1]);
            if (scenario == 5)
            {
                Assert.Equal(9, decision[1]);
                Assert.Equal(budget, decision[2]);
                Assert.Equal(0, work[2] + work[3]);
            }
            if (scenario == 6)
            {
                Assert.Equal(1, decision[0]);
                Assert.Equal(1, decision[3]);
            }
            // Valid 28 metre transmission retains its full weight, without a range fade.
            if (scenario == 7) Assert.Equal(1,decision[3]);
            if (scenario == 8)
            {
                Assert.True(transport[1] > 0);
                if (budget == 8) Assert.Equal(1, decision[0]);
            }
            if (scenario == 10) Assert.Equal(0,decision[0]);
            if (scenario == 11) Assert.Equal(0,decision[0]);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Projects an independently specified axial receiver distance into OpenGL device depth.</summary>
    private static float DeviceDepth(float metres) => .5f * (1 + (100.1f - 20 / metres) / 99.9f);

    #endregion
}
