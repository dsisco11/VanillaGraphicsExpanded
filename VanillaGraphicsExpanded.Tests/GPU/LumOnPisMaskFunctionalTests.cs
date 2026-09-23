using VanillaGraphicsExpanded.LumOn;
using System.Globalization;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks real importance-mask pixels from deterministic radiance, confidence and typed specialization inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnPisMaskFunctionalTests : LumOnShaderFunctionalTestBase
{
    /// <summary>Uses the shared headless rendering context.</summary>
    public LumOnPisMaskFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Rendering cases
    /// <summary>Only the enabled non-overridden branch chooses a bright direction outside the uniform batch.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void StructuralOverridesSelectExpectedMask(bool enabled, bool batch, bool uniform)
    {
        var result = Draw(enabled, batch, uniform, 0, 0, 1e-6f);
        ulong expected = enabled && !batch && !uniform ? (1UL << 36) | 0x7FUL : 0xFFUL;
        Assert.Equal(expected, result.Mask);
        Assert.True(result.Energy > 0);
    }

    /// <summary>Explicit counts, negative sentinel fractions and epsilon boundaries change actual selected texels.</summary>
    [Theory]
    [InlineData(-1, 0f, 1e-6f, false)]
    [InlineData(-1, 1f, 1e-6f, true)]
    [InlineData(8, 0f, 1e-6f, true)]
    [InlineData(100, 0f, 1e-6f, true)]
    [InlineData(0, 1f, 100f, true)]
    public void NumericSpecializationsControlExplorationAndImportance(int count, float fraction, float epsilon, bool uniform)
    {
        var result = Draw(true, false, false, count, fraction, epsilon);
        Assert.Equal(uniform ? 0xFFUL : (1UL << 36) | 0x7FUL, result.Mask);
    }
    #endregion

    #region Fixture inputs
    /// <summary>Lights one upward-facing octahedral texel beyond batch zero, making its inclusion directly observable.</summary>
    private (ulong Mask, float Energy) Draw(bool enabled, bool batch, bool uniform, int count, float fraction, float epsilon)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<LumOnProbeAtlasPisMaskShaderProgram>(shader =>
        {
            shader.ImportanceSampling = enabled; shader.BatchSlicing = batch; shader.UniformMask = uniform;
            shader.TexelsPerFrame = 8; shader.ExploreCount = count; shader.ExploreFraction = fraction; shader.WeightEpsilon = epsilon;
        });
        using var use = program.UseScope();
        var textures = new List<DynamicTexture2D>();
        try
        {
            var radiance = new float[16 * 16 * 4];
            for (int py = 0; py < 2; py++)
            for (int px = 0; px < 2; px++)
            for (int channel = 0; channel < 3; channel++) radiance[((py * 8 + 4) * 16 + px * 8 + 4) * 4 + channel] = 1;
            program.ProbeAnchorPosition = Create( 2, 2, CreateUniformColorData(2, 2, 0, 0, -5, 1));
            program.ProbeAnchorNormal = Create( 2, 2, CreateUniformColorData(2, 2, .5f, .5f, 1, 0));
            program.ScreenProbeAtlasHistory = Create( 16, 16, radiance);
            program.ScreenProbeAtlasMetaHistory = Create( 16, 16, CreateUniformColorData(16, 16, 1, 0, 0, 0));
            UpdateAndBindLumOnFrameUbo(program);
            using var output = TestFramework.CreateTestGBuffer(2, 2, PixelInternalFormat.Rg32f, PixelInternalFormat.R32f);
            TestFramework.RenderQuadTo(program, output);
            var mask = output[0].ReadPixels();
            uint low = BitConverter.SingleToUInt32Bits(mask[0]), high = BitConverter.SingleToUInt32Bits(mask[1]);
            float energy = output[1].ReadPixels()[0];
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            return (low | ((ulong)high << 32), energy);
        }
        finally
        {
            foreach (var texture in textures) texture.Dispose();

        }

        /// <summary>Binds a controlled floating point texture using the existing explicit resource contract.</summary>
        DynamicTexture2D Create(int width, int height, float[] data)
        {
            var texture = TestFramework.CreateTexture(width, height, PixelInternalFormat.Rgba32f, data);
            textures.Add(texture);
            return texture;
        }
    }
    #endregion
}
