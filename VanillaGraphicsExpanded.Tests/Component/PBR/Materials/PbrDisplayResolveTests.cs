using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;



using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;
/// <summary>Tests the explicit scene-linear to display boundary independently from lighting composition.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrDisplayResolveTests : LumOnShaderFunctionalTestBase
{
    #region Public API
    /// <summary>Uses the shared graphics context.</summary>
    public PbrDisplayResolveTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Display transfer
    /// <summary>HDR channels undergo one Reinhard and sRGB conversion; negative values clamp and alpha survives.</summary>
    [Fact]
    public void GeometryConvertsHdrExactlyOnceAndSkyPassesThrough()
    {
        // Arrange: Supply HDR geometry and an already-resolved sky pixel with distinct alpha values.
        // Act and assert: Resolve the image and verify geometry conversion once, negative clamping, and sky passthrough.
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRDisplayResolveShaderProgram>();
        using var scene = TestFramework.CreateTexture(3, 1, TextureFormat.Rgba16f,
            new[] { 0f, 1f, 4f, .5f, -.5f, .125f, 16f, 1f, .125f, .5f, .75f, .25f });
        using var depth = TestFramework.CreateTexture(3, 1, TextureFormat.R32f, new[] { .5f, .9999993f, 1f });
        using var output = TestFramework.CreateTestGBuffer(3, 1, TextureFormat.Rgba8);
        FramebufferTestSetup.BindForEngineRendering(output);
        {
            program.PrimaryScene = scene;
            program.PrimaryDepth = depth;
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.FramebufferSrgb);
            TestFramework.RenderQuad(program);
        }
        float[] expected = [Transfer(0, 4), Transfer(1, 4), Transfer(4, 4), .5f,
            Transfer(-.5f, 16), Transfer(.125f, 16), Transfer(16, 16), 1, .125f, .5f, .75f, .25f];
        ImageAssertions.Comparisons.CompareIntervals(output[0],expected.Select(v=>(double)(v-.0041f)).ToArray(),expected.Select(v=>(double)(v+.0041f)).ToArray()).AssertEqual();
        var actual=GpuLocalRead.Tuple(output[0],0,0);Assert.True(actual[2]>actual[1]);
    }
    /// <summary>Night, twilight and daylight retain linear RGB ratios under the common exposure and shoulder.</summary>
    [Theory]
    [InlineData(.00001f)]
    [InlineData(.01f)]
    [InlineData(1f)]
    [InlineData(100f)]
    [InlineData(10000f)]
    public void SharedExposurePreservesHueAcrossRadianceScales(float scale)
    {
        // Arrange: Scale scene radiance and publish the matching physical-lighting scale.
        // Act and assert: Resolve both scales with shared exposure and compare their hue-preserving output.
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRDisplayResolveShaderProgram>();
        using var scene = TestFramework.CreateTexture(2, 1, TextureFormat.Rgba32f,
            new[] { scale, scale * .25f, scale * .0625f, .37f, scale, scale * .25f, scale * .0625f, .37f });
        using var depth = TestFramework.CreateTexture(2, 1, TextureFormat.R32f, new[] { .5f, .5f });
        using var output = TestFramework.CreateTestGBuffer(2, 1, TextureFormat.Rgba32f);
        FramebufferTestSetup.BindForEngineRendering(output);
        {
            program.PrimaryScene = scene;
            program.PrimaryDepth = depth;
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.FramebufferSrgb);
            TestFramework.RenderQuad(program);
        }
        float[] actual = GpuLocalRead.Tuple(output[0],1,0);
        // Pixel (1,0) has rank 32; undo its known positive offset before checking the pure transfer.
        for (int channel = 0; channel < 3; channel++) actual[channel] -= .5f / (64f * 255f);
        float[] ratios = [1, .25f, .0625f];
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.InRange(actual[channel], 0f, 1f);
            float expected = Transfer(scale * ratios[channel], scale);
            Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
            Assert.InRange(Decode(actual[channel]) / Decode(actual[0]), ratios[channel] - .0001f, ratios[channel] + .0001f);
        }
        Assert.InRange(actual[3], .36999f, .37001f);
    }
    #endregion

    #region Quantized output dithering
    /// <summary>Quantization retains black and white endpoints and bypasses already-resolved sky pixels exactly.</summary>
    [Fact]
    public void EndpointsAndAlreadyResolvedSkyAreUnchanged()
    {
        // Arrange: Select exact black/white geometry endpoints and an already-resolved sky sample.
        // Act and assert: Run display resolve and require the endpoints and sky to remain unchanged.
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRDisplayResolveShaderProgram>();
        const int width = 24;
        float[] pixels = new float[width * 8 * 4];
        float[] depths = new float[width * 8];
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;
            float value = x < 8 ? -1f : x < 16 ? 1e10f : 100.4f / 255f;
            pixels[index * 4] = pixels[index * 4 + 1] = pixels[index * 4 + 2] = value;
            pixels[index * 4 + 3] = 73f / 255f;
            depths[index] = x < 16 ? .5f : 1f;
        }
        using var scene = TestFramework.CreateTexture(width, 8, TextureFormat.Rgba32f, pixels);
        using var depth = TestFramework.CreateTexture(width, 8, TextureFormat.R32f, depths);
        using var output = TestFramework.CreateTestGBuffer(width, 8, TextureFormat.Rgba8);
        FramebufferTestSetup.BindForEngineRendering(output);
        {
            program.PrimaryScene = scene;
            program.PrimaryDepth = depth;
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.FramebufferSrgb);
            TestFramework.RenderQuad(program);
        }
        var expectedBytes=new byte[width*8*4];
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = (y * width + x) * 4;
            int expected = x < 8 ? 0 : x < 16 ? 255 : 100;
            for (int channel = 0; channel < 3; channel++)
                expectedBytes[offset+channel]=(byte)expected;
            expectedBytes[offset+3]=73;
        }
        ImageAssertions.Comparisons.CompareRgba8(output[0],expectedBytes).AssertEqual();
    }

    /// <summary>Sub-code-value ramps occupy both neighboring codes with balanced neutral noise, unchanged alpha and deterministic output.</summary>
    [Fact]
    public void QuantizedRampIsBalancedNeutralAndDeterministic()
    {
        // Arrange: Build a sub-code neutral ramp whose dithered samples straddle neighboring output codes.
        // Act and assert: Resolve repeatedly and verify balanced neutral noise, unchanged alpha, and deterministic output.
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRDisplayResolveShaderProgram>();
        const int width = 64;
        const int height = 8;
        float[] pixels = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // Each tile moves one eighth of an output code, too little for undithered smooth output.
            float encoded = (100f + (x / 8 + .5f) / 8f) / 255f;
            float mapped = Decode(encoded);
            float radiance = mapped / (1f - mapped);
            int offset = (y * width + x) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = radiance;
            pixels[offset + 3] = 73f / 255f;
        }
        using var scene = TestFramework.CreateTexture(width, height, TextureFormat.Rgba32f, pixels);
        using var depth = TestFramework.CreateTexture(width, height, TextureFormat.R32f,
            Enumerable.Repeat(.5f, width * height).ToArray());
        using var output = TestFramework.CreateTestGBuffer(width, height, TextureFormat.Rgba8);
        using var preciseOutput = TestFramework.CreateTestGBuffer(width, height, TextureFormat.Rgba32f);
        using var precise = Render(preciseOutput);
        using var first = Render();
        using var second = Render();
        ImageAssertions.Comparisons.CompareHistory(second.Texture,first).AssertEqual();
        // An explicit reference matrix keeps the check independent of the shader's bit-interleaving loop.
        int[] ranks = [0,32,8,40,2,34,10,42, 48,16,56,24,50,18,58,26,
            12,44,4,36,14,46,6,38, 60,28,52,20,62,30,54,22,
            3,35,11,43,1,33,9,41, 51,19,59,27,49,17,57,25,
            15,47,7,39,13,45,5,37, 63,31,55,23,61,29,53,21];
        var projection = ImageAssertions.Projections;
        // Preserve the original float multiply/subtract and row-major float summation order.
        using var offsets=projection.Apply(precise.Texture, MaterialProjections.PbrDisplayResolveTests_QuantizedRampIsBalancedNeutralAndDeterministic);
        using var codes=projection.Apply(first.Texture, ObservationProjections.GpuImageAssertionsPredicates_CompareRgba8);
        var expectedOffsets=new float[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            expectedOffsets[(y*width+x)*4]=(ranks[y*8+x%8]+.5f)/64f-.5f;
        ImageAssertions.Comparisons.Compare(offsets,expectedOffsets,.0001f,channelMask:1).AssertEqual();
        ImageAssertions.Statistics.Inspect(offsets,-.493f,.493f,channelMask:1).AssertFiniteRange();
        ImageAssertions.Statistics.Inspect(codes,100,101,channelMask:1).AssertFiniteRange();
        ImageAssertions.Predicates.Predicate(first.Texture, MaterialPredicates.PbrDisplayResolveTests_QuantizedRampIsBalancedNeutralAndDeterministic_2).AssertEqual();
        int previousUpperCodes=-1;
        for(int tile=0;tile<8;tile++)
        {
            var region=new RHITextureRegion(new(0,0),tile*8,0,0,8,height,1);
            int upperCodes=checked((int)ImageAssertions.Statistics.Statistics(codes,region,1).Sum(0))-100*64;
            using var selectedOffsets=new GpuImageSnapshot(offsets,region);
            float offsetSum=ImageAssertions.Statistics.OrderedFloatSum(selectedOffsets.Texture, ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic);
            Assert.InRange(MathF.Abs(offsetSum/64f),0f,.0001f);
            Assert.True(upperCodes>previousUpperCodes);previousUpperCodes=upperCodes;
            Assert.InRange(MathF.Abs(upperCodes/64f-(tile+.5f)/8f),0f,1f/16f);
        }
        // A second draw must reproduce the exact pattern, without temporal noise.
        GpuImageSnapshot Render(VanillaGraphicsExpanded.Rendering.GpuFramebuffer? destination = null)
        {
            FramebufferTestSetup.BindForEngineRendering(destination ?? output);
            {
                program.PrimaryScene = scene;
                program.PrimaryDepth = depth;
                GL.Disable(EnableCap.Blend);
                GL.Disable(EnableCap.DepthTest);
                GL.Disable(EnableCap.FramebufferSrgb);
                TestFramework.RenderQuad(program);
            }
            return new GpuImageSnapshot((destination ?? output)[0]);
        }
    }
    #endregion

    #endregion

    #region Private
    /// <summary>Evaluates the specified display operator on CPU, with no shader helper reuse.</summary>
    private static float Transfer(float radiance, float peak)
    {
        float positive = Math.Max(0, radiance);
        float mapped = positive / (1 + peak);
        return mapped <= .0031308f ? mapped * 12.92f : 1.055f * MathF.Pow(mapped, 1 / 2.4f) - .055f;
    }
    /// <summary>Decodes display values to verify linear hue independently of sRGB encoding.</summary>
    private static float Decode(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-material", 0, 0);
        programs.Prepare("projection-material", 0, 0);
        programs.Prepare("projection-observation", 0, 0);
        programs.Prepare("statistics", 0, 0);
    }
    #endregion
}
