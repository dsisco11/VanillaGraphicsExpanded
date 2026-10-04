using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks geometry-aware reduction retains exact color, depth and original texel coordinates.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionReductionTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Reduction rejects float32 radiance that cannot survive its RGBA16F destination.</summary>
    [Theory]
    [InlineData(65504f, true)]
    [InlineData(65520f, false)]
    public void ReductionPreservesHalfFloatRepresentability(float radiance, bool accepted)
    {
        EnsureShaderTestAvailable();
        using var color = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(2, 2, PixelInternalFormat.R32f);
        color.UploadDataImmediate([8,2,1,1, 8,2,1,1, 8,2,1,1, radiance,2,1,1]);
        depth.UploadDataImmediate([.25f,.25f,.25f,.75f]);
        var program = Programs.Create<WaterRefractionReductionShaderProgram>();
        program.SourceColor = color; program.SourceDepth = depth;
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        float[] selected = target[0].ReadPixels(), provenance = target[1].ReadPixels();
        Assert.Equal(accepted ? radiance : 8, selected[0]);
        Assert.Equal(accepted ? .75f : .25f, provenance[0]);
        Assert.Equal(1, selected[3]);
        Assert.Equal(1, provenance[3]);
        Assert.All(selected, value => Assert.True(float.IsFinite(value)));
    }

    /// <summary>Each reduced footprint selects the farthest eligible receiver, including incomplete odd edges.</summary>
    [Theory]
    [InlineData(2, 2, false)]
    [InlineData(5, 3, false)]
    [InlineData(5, 3, true)]
    public void ReductionPreservesAssociatedReceiver(int width, int height, bool invalid)
    {
        EnsureShaderTestAvailable();
        using var color = DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(width, height, PixelInternalFormat.R32f);
        float[] colors = new float[width * height * 4];
        float[] depths = new float[width * height];
        for (int i = 0; i < depths.Length; i++)
        {
            depths[i] = .1f + i * .04f;
            colors[i * 4] = 4 + i; colors[i * 4 + 1] = 2 + i; colors[i * 4 + 2] = .5f;
            colors[i * 4 + 3] = invalid ? 0 : 1;
        }
        // Bright unsupported taps must never contaminate a valid neighboring receiver.
        depths[0] = float.NaN;
        depths[1] = 1;
        if (width > 2) colors[(width + 1) * 4 + 3] = 0;
        color.UploadDataImmediate(colors); depth.UploadDataImmediate(depths);
        var program = Programs.Create<WaterRefractionReductionShaderProgram>();
        program.SourceColor = color; program.SourceDepth = depth;
        int reducedWidth = (width + 1) / 2, reducedHeight = (height + 1) / 2;
        using var target = CreateMRTRenderTarget(reducedWidth, reducedHeight, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program, target);
        float[] actualColor = target[0].ReadPixels(), actualDepth = target[1].ReadPixels();
        for (int y = 0; y < reducedHeight; y++)
        for (int x = 0; x < reducedWidth; x++)
        {
            int selected = -1;
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                int sx = x * 2 + dx, sy = y * 2 + dy;
                if (sx >= width || sy >= height) continue;
                int candidate = sy * width + sx;
                if (!float.IsFinite(depths[candidate]) || depths[candidate] <= 0 || depths[candidate] >= .999999f
                    || colors[candidate * 4 + 3] < .5f) continue;
                if (selected < 0 || depths[candidate] > depths[selected]) selected = candidate;
            }
            int destination = (y * reducedWidth + x) * 4;
            if (selected < 0)
            {
                Assert.Equal(0, actualColor[destination + 3]);
                Assert.Equal(0, actualDepth[destination + 3]);
                continue;
            }
            for (int channel = 0; channel < 4; channel++) Assert.Equal(colors[selected * 4 + channel], actualColor[destination + channel]);
            Assert.Equal(depths[selected], actualDepth[destination]);
            Assert.InRange(MathF.Abs(actualDepth[destination + 1] - (selected % width + .5f) / width), 0, 1e-6f);
            Assert.InRange(MathF.Abs(actualDepth[destination + 2] - (selected / width + .5f) / height), 0, 1e-6f);
            Assert.Equal(1, actualDepth[destination + 3]);
        }
    }
    #endregion
}
