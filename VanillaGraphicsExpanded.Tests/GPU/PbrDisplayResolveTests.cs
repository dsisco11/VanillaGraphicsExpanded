using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Tests the explicit scene-linear to display boundary independently from lighting composition.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrDisplayResolveTests : LumOnShaderFunctionalTestBase
{
    /// <summary>Uses the shared graphics context.</summary>
    public PbrDisplayResolveTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Display transfer
    /// <summary>HDR channels undergo one Reinhard and sRGB conversion; negative values clamp and alpha survives.</summary>
    [Fact]
    public void GeometryConvertsHdrExactlyOnceAndSkyPassesThrough()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<PBRDisplayResolveShaderProgram>();
        using var scene = TestFramework.CreateTexture(3, 1, PixelInternalFormat.Rgba16f,
            new[] { 0f, 1f, 4f, .5f, -.5f, .125f, 16f, 1f, .125f, .5f, .75f, .25f });
        using var depth = TestFramework.CreateTexture(3, 1, PixelInternalFormat.R32f, new[] { .5f, .9999993f, 1f });
        using var output = TestFramework.CreateTestGBuffer(3, 1, PixelInternalFormat.Rgba8);
        output.BindWithViewport();
        using (program.UseScope())
        {
            program.PrimaryScene = scene.TextureId;
            program.PrimaryDepth = depth.TextureId;
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.FramebufferSrgb);
            TestFramework.RenderQuad(program);
        }
        float[] actual = output[0].ReadPixels();
        float[] expected = [Transfer(0), Transfer(1), Transfer(4), .5f,
            Transfer(-.5f), Transfer(.125f), Transfer(16), 1, .125f, .5f, .75f, .25f];
        for (int i = 0; i < expected.Length; i++) Assert.InRange(actual[i], expected[i] - .0041f, expected[i] + .0041f);
        Assert.True(actual[2] > actual[1]);
    }
    #endregion

    #region Independent transfer reference
    /// <summary>Evaluates the specified display operator on CPU, with no shader helper reuse.</summary>
    private static float Transfer(float radiance)
    {
        float positive = Math.Max(0, radiance);
        float mapped = positive / (1 + positive);
        return mapped <= .0031308f ? mapped * 12.92f : 1.055f * MathF.Pow(mapped, 1 / 2.4f) - .055f;
    }
    #endregion
}
