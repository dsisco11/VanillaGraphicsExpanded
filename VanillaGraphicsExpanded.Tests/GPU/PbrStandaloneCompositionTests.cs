using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks standalone environment response independently from LumOn indirect lighting.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrStandaloneCompositionTests : LumOnShaderFunctionalTestBase
{
    /// <summary>Uses the shared shader and render-target test owners.</summary>
    public PbrStandaloneCompositionTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Lighting modes
    /// <summary>Zero availability stays dark; rough metals and dielectrics differ without double-adding GI.</summary>
    [Theory]
    [InlineData(false, 0f, 0f, .5f)]
    [InlineData(false, .5f, 0f, .5f)]
    [InlineData(false, .5f, 1f, 0f)]
    [InlineData(false, .5f, 1f, 1f)]
    [InlineData(true, .5f, 0f, .5f)]
    public void ModesUseOnlyTheirOwnedEnvironment(bool lumon, float availability, float metallic, float roughness)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRCompositeShaderProgram>(p =>
        {
            p.LumOnEnabled = lumon; p.EnablePbrComposite = false; p.EnableShortRangeAo = false;
        });
        using var zero = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new float[4]);
        using var emission = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { .25f, .25f, .25f, 1f });
        using var indirect = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { .75f, .75f, .75f, 1f });
        using var albedo = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { .5f, .5f, .5f, 1f });
        using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { .5f, .5f, 1f, 1f });
        using var material = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { roughness, metallic, 0f, 0f });
        using var environment = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { availability, availability, availability, 1f });
        using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { .25f });
        using var output = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f);
        using var position = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new float[4]);
        program.GBufferPosition = position.TextureId;
        output.BindWithViewport();
        {
            using var directLighting = LayeredTestTexture.Create(zero, zero, emission);
            program.DirectLighting = directLighting;
            program.IndirectDiffuse = indirect; program.GBufferAlbedo = albedo.TextureId;
            using var surface = LayeredTestTexture.Create(normal, material, environment);
            program.GBufferSurface = surface; program.PrimaryDepth = depth.TextureId;

            using var frameCamera = TestFrameCamera.Create([1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1], [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]);
            program.FrameInputs = frameCamera;

            program.IndirectIntensity = 1; program.IndirectTint = new(1,1,1);
            TestFramework.RenderQuadTo(program, output);
            if (!lumon)
            {
                int active = GL.GetInteger(GetPName.ActiveTexture);
                GL.ActiveTexture(TextureUnit.Texture6);
                int bound = GL.GetInteger(GetPName.TextureBinding2DArray);
                GL.ActiveTexture((TextureUnit)active);
                Assert.Equal(surface.TextureId, bound);
            }
        }
        float f0 = metallic == 1 ? .5f : .04f;
        float response = lumon ? .75f * .5f : availability * ((1 - f0) * (1 - metallic) * .5f + f0 * (1 - roughness));
        foreach (float actual in output[0].ReadPixels().Take(3)) Assert.InRange(actual, .25f + response - .001f, .25f + response + .001f);
    }
    #endregion
}
