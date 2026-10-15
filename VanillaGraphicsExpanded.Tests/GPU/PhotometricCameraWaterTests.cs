using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks raw accessor lighting at the actual camera-medium composition boundary.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PhotometricCameraWaterTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Signed boundary RGB decodes before nonzero camera illumination while optical length and winding remain unscaled.</summary>
    [Fact]
    public void SignedBoundaryAndCameraSourceAreIndependentOfFrameScale()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRCompositeShaderProgram>(p =>
        { p.LumOnEnabled = false; p.EnablePbrComposite = false; p.EnableShortRangeAo = false; });
        using var zero = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new float[4]);
        using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, [.75f]);
        using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [.5f, 1f, .5f, 1]);
        using var surface = LayeredTestTexture.Create(normal, zero, zero);
        using var optical = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [-.7f, -1.4f, -2.1f, -7]);
        using var output = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        using var camera = TestFrameCamera.Create([1,0,0,0, 0,1,0,0, 0,0,20,0, 0,0,0,1],
            [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]);
        using var publication = new TestFrameLightingPublication();
        program.FrameLightingInputs = publication.Buffer;
        program.FrameInputs = camera; program.GBufferSurface = surface;
        program.IndirectDiffuse = zero; program.GBufferAlbedo = zero.TextureId;
        program.GBufferPosition = zero.TextureId; program.PrimaryDepth = depth.TextureId;
        program.SetAtmosphere(null);
        var medium = new WaterMedium(new(.1f, .2f, .3f), Vector3.Zero);
        var cameraSource = new Vector3(.3f, .4f, .5f);
        float[]? reference = null;
        foreach (float scale in new[] { 1f, .25f })
        {
            publication.Capture(scale);
            using var direct = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [2*scale, scale, .5f*scale, 1]);
            using var lighting = LayeredTestTexture.Create(direct, zero, zero);
            using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [-2.1f*scale, -2.8f*scale, -3.5f*scale, -1]);
            using var water = LayeredTestTexture.Create(optical, source);
            program.DirectLighting = lighting;
            program.SetWaterVolume(new(water, medium, cameraSource, cameraSource));
            program.SetUnderwater(true);
            TestFramework.RenderQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            for (int channel = 0; channel < 3; channel++)
            {
                float physical = actual[channel] / scale;
                Assert.True(float.IsFinite(physical) && physical > 0);
                if (reference is not null) Assert.InRange(Math.Abs(physical - reference[channel]), 0, .00001f);
            }
            reference = actual.Take(3).Select(value => value / scale).ToArray();
            Assert.Equal(-7, optical.ReadPixels()[3]);
            Assert.Equal(-1, source.ReadPixels()[3]);
        }
    }
    /// <summary>Raw fields reproduce separately calibrated physical scattering, with no extra decoder or environment contribution.</summary>
    [Theory]
    [InlineData(false, .25f, true)]
    [InlineData(true, .25f, true)]
    [InlineData(false, 1f, true)]
    [InlineData(true, 1f, true)]
    [InlineData(false, .625f, true)]
    [InlineData(true, 1f, false)]
    public void RawCameraLightMatchesPhysicalSource(bool blockFull, float skyEncoded, bool atmosphereAvailable)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRCompositeShaderProgram>(p =>
        { p.LumOnEnabled = false; p.EnablePbrComposite = false; p.EnableShortRangeAo = false; });
        using var zero = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new float[4]);
        using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, [.75f]);
        using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [.5f, 1f, .5f, 1f]);
        using var lighting = LayeredTestTexture.Create(zero, zero, zero);
        using var surface = LayeredTestTexture.Create(normal, zero, zero);
        using var water = LayeredTestTexture.Create(zero, zero);
        using var output = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        using var camera = TestFrameCamera.Create([1,0,0,0, 0,1,0,0, 0,0,20,0, 0,0,0,1],
            [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]);
        camera.SetLightingCalibration(new(8, .2f, AccessorSkyFloor: .25f,
            AccessorBlockReferenceLux: 120, AccessorBlockNeutralFloor: .1f));
        program.FrameInputs = camera;
        program.DirectLighting = lighting; program.GBufferSurface = surface;
        program.IndirectDiffuse = zero; program.GBufferAlbedo = zero.TextureId;
        program.GBufferPosition = zero.TextureId; program.PrimaryDepth = depth.TextureId;
        using var owner = new AtmosphereModSystem();
        var environment = new Vector3(3, 5, 7);
        var snapshot = new AtmosphereLighting(Vector3.UnitY, Vector3.Zero, environment,
            Vector3.Zero, Vector3.Zero, ImmutableArray.Create(0f,0f,0f,1f)) { Width = 1, Height = 1 };
        owner.Publish(snapshot);
        program.SetAtmosphere(atmosphereAvailable ? AtmosphereModSystem.Lighting : null);
        var medium = new WaterMedium(Vector3.Zero, new(.2f, .3f, .4f));
        var raw = new Vector3(1, .5f, .25f) * (blockFull ? 1 : .1f);
        // This independent reference follows the accessor toe and environment lux contract directly.
        float peak = blockFull ? 1 : .1f;
        float amplitude = MathF.Max(peak - .1f, 0);
        Vector3 blockLux = raw * (MathF.Pow(amplitude, 4) / (MathF.Pow(amplitude, 3) + .064f) / peak) * 120;
        float availability = Math.Clamp((skyEncoded - .25f) / .75f, 0, 1);
        Vector3 incident = blockLux + (atmosphereAvailable ? environment * (MathF.PI * availability) : Vector3.Zero);
        Vector3 physicalSource = medium.EffectiveScatteringPerMetre * incident / (4 * MathF.PI);
        program.SetWaterVolume(new(water, medium, SourceUpper: new Vector3(10000),
            RawCameraLight: new Vector4(raw, skyEncoded)));
        program.SetUnderwater(true);
        TestFramework.RenderQuadTo(program, output);
        float[] actual = output[0].ReadPixels();
        program.SetWaterVolume(new(water, medium, physicalSource, new Vector3(10000)));
        TestFramework.RenderQuadTo(program, output);
        float[] expected = output[0].ReadPixels();
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.True(float.IsFinite(actual[channel]));
            Assert.InRange(Math.Abs(actual[channel] - expected[channel]), 0, .0001f);
            if (incident[channel] > 0) Assert.True(actual[channel] > 0);
            else Assert.Equal(0, actual[channel]);
        }
    }
    #endregion
}
