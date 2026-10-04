using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes the production color helper using precompiled SPIR-V and independent numerical references.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorOutputTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Linear output bypasses tone mapping and dither; legacy output retains both operations.</summary>
    [Fact]
    public void SharedOutputRetainsHdrAndLegacyDisplay()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<SceneColorOutputShaderProgram>();
        using var target = CreateRenderTarget(2, 1, PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        float[] actual = target[0].ReadPixels();
        Assert.Equal(new[] { 8f, 2f, 0f, .375f }, actual[..4]);
        // Pixel one has independent Bayer rank 32: its positive offset is half a rank.
        float dither = .5f / (64f * 255f);
        float red = 1.055f * MathF.Pow(8f / 9f, 1f / 2.4f) - .055f + dither;
        float green = 1.055f * MathF.Pow(2f / 9f, 1f / 2.4f) - .055f + dither;
        Assert.InRange(actual[4], red - .00001f, red + .00001f);
        Assert.InRange(actual[5], green - .00001f, green + .00001f);
        Assert.InRange(actual[6], dither - .000001f, dither + .000001f);
        Assert.Equal(.375f, actual[7]);
    }
    #endregion
}
