using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR.Materials;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.PBR.Postprocessing;



using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Integration.PBR.Materials;
/// <summary>Checks material atlas publication and the packaged material parameter sampler.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrMaterialParamsTextureSmokeTests : RenderTestBase
{
    #region Public API

    /// <summary>Uses the shared headless rendering context.</summary>
    /// <param name="fixture">The shared OpenGL fixture and GPU service owner.</param>
    public PbrMaterialParamsTextureSmokeTests(HeadlessGLFixture fixture) : base(fixture)
    {
    }

    /// <summary>Production atlas allocation preserves independent transmission alongside existing RGB properties.</summary>
    [Fact]
    public void MaterialAtlasPublishesTransmissionInAlpha()
    {
        // Arrange: initialize the fixture-owned OpenGL services for this test.
        EnsureContextValid();
        using var store = new MaterialAtlasTextureStore();

        // Act: publish one atlas page and upload its packed transmission tuple.
        store.SyncToAtlasPages([(101, 1, 1)], false);
        Assert.True(store.TryGetPageTextures(101, out var page));
        Assert.Equal(TextureFormat.Rgba16f, page.MaterialParamsTexture.InternalFormat);
        Assert.Equal(0f, Assertions.GpuLocalRead.Scalar(page.MaterialParamsTexture, 3));
        page.MaterialParamsTexture.UploadDataImmediate(MaterialTransmission.Pack([.25f, .5f, .75f], .35f), 0, 0, 1, 1);
        float[] actual = page.MaterialParamsTexture.ReadPixels();
        float[] expected = [.25f, .5f, .75f, .35f];

        // Assert: preserve each packed channel and require a clean native error state.
        for (int channel = 0; channel < 4; channel++)
            Assert.InRange(actual[channel], expected[channel] - .001f, expected[channel] + .001f);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Samples the existing RGB16F texture through its typed fixture pass without GL errors.</summary>
    [Fact]
    public void Rgb16fTexture_CanSampleWithoutGlErrors()
    {
        // Arrange: the base fixture opens the uniform allocation epoch required by production shader binding.
        EnsureContextValid();

        using var framework = new ShaderTestFramework();

        const int width = 4;
        const int height = 4;

        // Arrange: retain the original RGB16F extent, tuple values and texture/readback checks.
        // RGB16F texture: (roughness, metallic, emissive)
        var data = new float[width * height * 3];
        for (int i = 0; i < data.Length; i += 3)
        {
            data[i + 0] = 0.25f;
            data[i + 1] = 0.50f;
            data[i + 2] = 0.75f;
        }

        using DynamicTexture2D materialTex = framework.CreateTexture(width, height, TextureFormat.Rgb16f, data, TextureFilterMode.Nearest);

        Assert.True(materialTex.IsValid);
        Assert.Equal(width, materialTex.Width);
        Assert.Equal(height, materialTex.Height);
        Assert.Equal(TextureFormat.Rgb16f, materialTex.InternalFormat);

        // The existing smoke oracle nominates only the first material tuple.
        var uploaded = materialTex.ReadPixelsRegion(0,0,1,1);
        Assert.Equal(3, uploaded.Length);
        Assert.Equal(0.25f, uploaded[0], 2);
        Assert.Equal(0.50f, uploaded[1], 2);
        Assert.Equal(0.75f, uploaded[2], 2);

        // Clear any prior GL error before the typed owner and pass submit this sample.
        _ = GL.GetError();

        using var output = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba16f, 1);

        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<BloomShaderProgram>();
        program.SourceImage = materialTex;
        program.SecondaryImage = materialTex;
        program.CaptureExtraction(true, false, 0, 0, 0, 0);
        var commands = RHICommandContext.Current;
        using var draw = new PostprocessDraw();
        var pipeline = draw.Prepare(program, output.PostprocessPass());
        using var boundary = commands.EnterBoundary(new("Tests.Rgb16f.Sampling", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        // Act: sample the RGB16F source with the production bloom executable and RHI pass submission.
        boundary.Run(() => draw.Submit(commands, pipeline, output.PostprocessPass(), program.CaptureBindings()));

        // Assert: both native error state and successful output read remain observable.
        Assert.Equal(ErrorCode.NoError, GL.GetError());

        // Smoke: the production bloom sampler consumes the RGB16F input without GL errors.
        _ = output[0].ReadPixels();
    }

    #endregion
}
