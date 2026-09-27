using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Constrains lighting composition before the separate display resolve.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrCompositeHdrTests : LumOnShaderFunctionalTestBase
{
    /// <summary>Uses the shared graphics context.</summary>
    public PbrCompositeHdrTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Scene-linear composition
    /// <summary>Radiance remains linear through composition, physical aerial transport, enclosure gating and decoded engine fog.</summary>
    [Theory]
    [InlineData(0f, false, 1f)]
    [InlineData(0.5f, false, 1f)]
    [InlineData(0f, true, 1f)]
    [InlineData(0f, true, 0f)]
    public void CompositePreservesHdrAndMixesLinearFog(float fog, bool atmosphere, float skyVisibility)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRCompositeShaderProgram>(p =>
        {
            p.LumOnEnabled = false; p.EnablePbrComposite = false; p.EnableShortRangeAo = false;
        });
        using var direct = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 2f, 1f, .5f, 1f });
        using var specular = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { .5f, .25f, .125f, 1f });
        using var emission = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { 4f, 2f, 1f, 1f });
        using var depth = TestFramework.CreateTexture(1, 1, PixelInternalFormat.R32f, new[] { .75f });
        using var unused = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] { .1f, .2f, .3f, 1f });
        using var environment = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new[] {0f,0f,0f,skyVisibility});
        using var output = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f);
        using var atmosphereOwner = new AtmosphereModSystem();
        float[] scatter = atmosphere ? [.2f,.3f,.4f,1f] : [0f,0f,0f,1f];
        float[] loss = atmosphere ? [.5f,.6f,.7f,1f] : [0f,0f,0f,1f];
        var snapshot = new AtmosphereLighting(Vector3.UnitY, Vector3.One, Vector3.Zero, Vector3.Zero, Vector3.Zero, ImmutableArray.Create(0f,0f,0f,1f))
        { Width=1, Height=1, AerialRadiance=ImmutableArray.CreateRange(Enumerable.Range(0,24).SelectMany(_=>scatter)), AerialAttenuation=ImmutableArray.CreateRange(Enumerable.Range(0,24).SelectMany(_=>loss)) };
        atmosphereOwner.Publish(snapshot);
        output.BindWithViewport();
        using (program.UseScope())
        {
            program.DirectDiffuse = direct; program.DirectSpecular = specular; program.Emissive = emission;
            program.IndirectDiffuse = unused; program.GBufferAlbedo = unused.TextureId;
            program.GBufferMaterial = unused.TextureId; program.GBufferNormal = unused.TextureId;
            program.PrimaryDepth = depth.TextureId;
            program.GBufferEnvironment = environment.TextureId;
            program.InvProjectionMatrix = [1,0,0,0, 0,1,0,0, 0,0,2000,0, 0,0,0,1];
            program.SetAtmosphere(snapshot);
            program.SetUnderwater(fog > 0);
            program.ViewMatrix = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
            program.FogDensityIn = 0; program.FogMinIn = fog; program.RgbaFogIn = new(.5f, .25f, .125f, 1);
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.FramebufferSrgb);
            TestFramework.RenderQuad(program);
        }
        var actual = output[0].ReadPixels();
        float[] light = [6.5f, 3.25f, 1.625f];
        float[] fogColor = [.5f, .25f, .125f];
        for (int channel = 0; channel < 3; channel++)
        {
            float transmission = 1 - loss[channel] * skyVisibility;
            float expected = (light[channel] * transmission + scatter[channel] * skyVisibility) * (1 - fog) + Linear(fogColor[channel]) * fog;
            Assert.InRange(actual[channel], expected - .004f, expected + .004f);
        }
        Assert.True(actual[0] > 1);
    }
    #endregion

    #region Transfer reference
    /// <summary>Decodes the documented sRGB fog input independently.</summary>
    private static float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    #endregion
}
