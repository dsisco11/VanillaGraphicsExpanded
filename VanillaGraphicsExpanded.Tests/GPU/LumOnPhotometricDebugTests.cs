using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks physical debug observations through the actual installed programs at differing frame scales.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnPhotometricDebugTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Persistent atlas radiance previews restore the fixed storage scale at the installed sampling boundary.</summary>
    [Theory]
    [InlineData(LumOnDebugMode.ProbeAtlasTraceRadiance)]
    [InlineData(LumOnDebugMode.ProbeAtlasCurrentRadiance)]
    [InlineData(LumOnDebugMode.ProbeAtlasFilteredRadiance)]
    [InlineData(LumOnDebugMode.ProbeAtlasGatherInputRadiance)]
    public void AtlasPreviewDecodesPersistentRadiance(LumOnDebugMode mode)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgramFamily.GetProgramName(mode));
        UpdateAndBindLumOnFrameUbo(program, screenWidth: 1, screenHeight: 1);
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [.125f, .0625f, .03125f, 1]);
        program.ProbeAtlasTrace = source; program.ProbeAtlasCurrent = source;
        program.ProbeAtlasFiltered = source; program.ProbeAtlasGatherInput = source;
        using var output = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, output);
        float[] actual = output[0].ReadPixels();
        Assert.Equal(new[] { 2f, 1f, .5f, 1f }, actual);
    }

    /// <summary>Radiance overlay tones the physical quantity at both unit and nonunit frame storage scales.</summary>
    [Fact]
    public void OverlayDecodesFrameRadianceBeforeTonemap()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgramFamily.GetProgramName(LumOnDebugMode.RadianceOverlay));
        UpdateAndBindLumOnFrameUbo(program, screenWidth: 1, screenHeight: 1);
        using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, [.75f]);
        using var publication = new TestFrameLightingPublication();
        program.FrameLightingInputs = publication.Buffer; program.PrimaryDepth = depth;
        using var output = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        foreach (float scale in new[] { 1f, .25f })
        {
            publication.Capture(scale);
            using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [2*scale, scale, .5f*scale, 1]);
            program.IndirectHalf = source;
            TestFramework.RenderQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            double[] expected = [2d/3, .5, 1d/3, 1];
            for (int channel = 0; channel < 4; channel++)
                Assert.InRange(actual[channel], expected[channel]-.000002, expected[channel]+.000002);
        }
    }
    /// <summary>Deferred radiance and the paired world-probe difference decode storage exactly once before display arithmetic.</summary>
    [Theory]
    [InlineData(LumOnDebugMode.DirectDiffuse)]
    [InlineData(LumOnDebugMode.WorldProbeLightingEffect)]
    public void PhysicalObservationIsIndependentOfFrameScale(LumOnDebugMode mode)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgramFamily.GetProgramName(mode));
        UpdateAndBindLumOnFrameUbo(program, screenWidth: 1, screenHeight: 1);
        program.DebugMode = (int)mode;
        program.WorldProbeEffectGain = 3;
        using var publication = new TestFrameLightingPublication();
        program.FrameLightingInputs = publication.Buffer;
        using var output = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        float[]? earlier = null;
        foreach (float scale in new[] { 1f, .25f })
        {
            // Only the encoded sources and frozen multiplier change; their physical RGB remains identical.
            publication.Capture(scale);
            using var full = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [2 * scale, scale, .5f * scale, 1]);
            using var suppressed = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [.5f * scale, .25f * scale, .125f * scale, 1]);
            using var direct = LayeredTestTexture.Create(full, null, null);
            program.DirectLighting = direct;
            program.IndirectDiffuseFull = full;
            program.WorldProbeSuppressedLighting = suppressed;
            TestFramework.RenderQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            double luminance = 1.5 * .2126 + .75 * .7152 + .375 * .0722;
            double brightness = luminance * 3 / (1 + luminance * 3);
            double[] expected = mode == LumOnDebugMode.DirectDiffuse
                ? [2d / 3, .5, 1d / 3, 1]
                : [brightness, brightness * .35, 0, 1];
            for (int channel = 0; channel < 4; channel++)
            {
                Assert.InRange(actual[channel], expected[channel] - .000002, expected[channel] + .000002);
                if (earlier is not null) Assert.InRange(actual[channel], earlier[channel] - .000002, earlier[channel] + .000002);
            }
            earlier = actual;
        }
    }
    #endregion
}
