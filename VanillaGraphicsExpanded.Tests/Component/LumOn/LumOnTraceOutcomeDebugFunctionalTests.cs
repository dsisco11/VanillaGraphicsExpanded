using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Validates the debug palette from packed raw trace metadata on the GPU.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnTraceOutcomeDebugFunctionalTests : LumOnShaderFunctionalTestBase
{
    #region Public API
    /// <summary>Uses the shared OpenGL context.</summary>
    public LumOnTraceOutcomeDebugFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

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
        // Arrange packed outcome metadata with unrelated flag bits set and bind the matching debug view.
        EnsureShaderTestAvailable();
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgramFamily.GetProgramName(LumOnDebugMode.ProbeAtlasTraceOutcome));
        {
            uint flags = (outcome << 16) | (1u << 8) | (1u << 14) | 3u;
            using var metadata = TestFramework.CreateTexture(1, 1, TextureFormat.Rg32f,
                new float[] { 0f, BitConverter.UInt32BitsToSingle(flags) });
            using var output = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f);
            program.DebugMode = 69;
            UpdateAndBindLumOnFrameUbo(program);
            program.ProbeAtlasMeta = metadata;
            // Act by rendering the trace outcome view over the packed metadata.
            TestFramework.RenderQuadTo(program, output);
            // Assert the displayed color matches the outcome's palette entry.
            ImageAssertions.Comparisons.CompareVector(output[0],new(red,green,blue,1f)).AssertEqual();
        }
    }

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }

    #endregion

}
