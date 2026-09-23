using VanillaGraphicsExpanded.LumOn;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Validates the debug palette from packed raw trace metadata on the GPU.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnTraceOutcomeDebugFunctionalTests : LumOnShaderFunctionalTestBase
{
    #region Construction
    /// <summary>Uses the shared OpenGL context.</summary>
    public LumOnTraceOutcomeDebugFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }
    #endregion

    #region Debug Palette
    /// <summary>Outcome colors ignore unrelated temporal and classification bits in the same metadata word.</summary>
    [Theory]
    [InlineData(0u, 0f, 0f, 0f)]
    [InlineData(1u, 1f, 0f, 0f)]
    [InlineData(2u, 0f, 1f, 0f)]
    [InlineData(3u, 1f, 1f, 0f)]
    [InlineData(4u, 1f, 0f, 1f)]
    [InlineData(5u, 0f, 1f, 1f)]
    [InlineData(6u, 0.25f, 0.5f, 1f)]
    public void TraceOutcomeDebug_MapsRecordedOutcome(uint outcome, float red, float green, float blue)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgram.ProbeAtlasContract.Identity);
        using var use = program.UseScope();
        {
            uint flags = (outcome << 16) | (1u << 8) | (1u << 14) | 3u;
            using var metadata = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rg32f,
                new float[] { 0f, BitConverter.UInt32BitsToSingle(flags) });
            using var output = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);
            program.DebugMode = 69;
            UpdateAndBindLumOnFrameUbo(program);
            program.ProbeAtlasMeta = metadata;
            TestFramework.RenderQuadTo(program, output);
            var pixels = output[0].ReadPixels();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                Assert.Equal(red, pixels[i]);
                Assert.Equal(green, pixels[i + 1]);
                Assert.Equal(blue, pixels[i + 2]);
                Assert.Equal(1f, pixels[i + 3]);
            }
        }
    }
    #endregion
}
