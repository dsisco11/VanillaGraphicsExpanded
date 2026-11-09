using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;

using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Exercises isolated particle blending, depth separation and borrowed image lifetimes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SceneColorParticleTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{


    #region Public API
    /// <summary>Ordered radiance survives without overwriting material data; later opaque depth owns replacement pixels.</summary>
    [Theory]
    [InlineData(0, TextureFormat.DepthComponent32)]
    [InlineData(0, TextureFormat.DepthComponent32f)]
    [InlineData(1, TextureFormat.DepthComponent32)]
    [InlineData(2, TextureFormat.DepthComponent32)]
    [InlineData(3, TextureFormat.DepthComponent32)]
    public void CaptureAndResolveSeparateMaterialDepthAndParticleRadiance(int scenario, TextureFormat depthFormat)
    {
        // Arrange
        EnsureShaderTestAvailable();
        using var equations = new NativeParticleBlendEquations();
        EstablishScissor(false);
        using var material = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba32f);
        using var glow = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba32f);
        using var depthStorage = new DepthTexture(2, 2, depthFormat);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var primary = DynamicGpuFramebuffer.Create([material, glow], depth);
        FramebufferTestSetup.BindForEngineRendering(primary);
        GL.Disable(EnableCap.ScissorTest);
        GL.DepthMask(true);
        GL.ColorMask(true, true, true, true);
        GL.ClearBuffer(ClearBuffer.Color, 0, new[] { .125f, .25f, .5f, 1f });
        GL.ClearBuffer(ClearBuffer.Color, 1, new[] { .1f, .2f, .3f, .4f });
        GL.ClearBuffer(ClearBuffer.Depth, 0, new[] { .75f });
        OpenGlStateCache.Current.InvalidateAll();
        EstablishScissor(false);
        using var capture = new SceneColorParticleTargets(primary);
        Assert.IsType<DynamicGpuFramebuffer>(capture.DrawTarget);
        Assert.IsType<RHIRenderPassDescription>(capture.ResolvePass);
        Assert.Equal(depth.TextureId, capture.VisibilityDepth.TextureId);
        Assert.False(capture.Captured);
        // Act
        capture.BeginCapture();
        Assert.Equal(.75f, ReadDepth(capture.BeforeDepth.TextureId));
        using var inputs = new PackedUniformBuffer(32);
        using var draw = BuiltShaderFixture.LoadFixtureProgram("tests/complete-state.vsh", "tests/particle-draw.fsh");
        using var vao = NativeResourceSetup.CreateVertexArray();
        FramebufferTestSetup.BindForEngineRendering(capture.DrawTarget);
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Less);
        GL.DepthMask(true);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);
        OpenGlStateCache.Current.Apply(SceneColorParticleDrawScope.CapturePipeline);
        GL.UseProgram(draw.Program.ProgramId);
        GL.BindVertexArray(vao.VertexArrayId);
        if (scenario != 3)
        {
            DrawParticle(inputs, 8, 0, scenario == 2 ? 0 : .5f, .625f);
            DrawParticle(inputs, 0, 4, scenario == 2 ? 0 : .5f, .5f);
        }
        OpenGlStateCache.Current.InvalidateAll();
        EstablishScissor(false);
        capture.EndCapture();
        // Assert
        Assert.True(capture.Captured);
        Assert.Equal(.75f, ReadDepth(capture.BeforeDepth.TextureId));
        float visibilityAfterDraw = ReadDepth(depth.TextureId);
        // Fixed-point depth32 conversion can round the nominal fragment value by one float ULP.
        // The actual before/after copies must nevertheless match the source representation exactly.
        Assert.InRange(visibilityAfterDraw, (scenario == 3 ? .75f : .5f) - .0000001f,
            (scenario == 3 ? .75f : .5f) + .0000001f);
        Assert.Equal(visibilityAfterDraw, ReadDepth(capture.AfterDepth.TextureId));
        float[] captured = capture.DrawTarget[0].ReadPixelsRegion(0,0,1,1);
        float[] expected = scenario >= 2 ? [0, 0, 0, 0] : [2, 2, 0, .75f];
        Assert.Equal(expected, captured[..4]);
        Assert.Equal(new[] { .125f, .25f, .5f, 1f }, primary[0].ReadTexel()[..4]);
        Assert.Equal(scenario == 3 ? new[] { .1f, .2f, .3f, .4f } : new[] { .8f, .7f, .6f, .5f }, primary[1].ReadTexel()[..4]);
        if (scenario == 1)
        {
            primary.Bind();
            GL.DepthMask(true);
            GL.ClearBuffer(ClearBuffer.Depth, 0, new[] { .25f });
            OpenGlStateCache.Current.InvalidateAll();
            EstablishScissor(false);
        }
        var resolve = Programs.Create<SceneColorParticleShaderProgram>();
        resolve.VisibilityDepth = capture.VisibilityDepth;
        resolve.BeforeDepth = capture.BeforeDepth;
        resolve.AfterDepth = capture.AfterDepth;
        resolve.ParticleColor = capture.ParticleColor;
        // Act
        TestFramework.RenderQuadTo(resolve, capture.ResolvePass);
        // Assert
        Assert.Equal(scenario == 1 ? ReadDepth(depth.TextureId) : ReadDepth(capture.BeforeDepth.TextureId), capture.MaterialDepth.ReadTexel()[0]);
        Assert.Equal(scenario == 1 ? new float[4] : expected, capture.VisibleRadiance.ReadTexel()[..4]);
        Assert.InRange(ReadDepth(depth.TextureId), (scenario == 1 ? .25f : visibilityAfterDraw) - .0000001f, (scenario == 1 ? .25f : visibilityAfterDraw) + .0000001f);
        // Act
        capture.ResetCapture();
        // Assert
        Assert.False(capture.Captured);
        Assert.Throws<InvalidOperationException>(capture.EndCapture);
        var retainedColor = capture.ParticleColor;
        // Act
        capture.BeginCapture();
        capture.EndCapture();
        // Assert
        Assert.Equal(retainedColor, capture.ParticleColor);
        ImageAssertions.Comparisons.CompareConstant(capture.DrawTarget[0], 0).AssertEqual();
        Assert.Equal(ReadDepth(capture.BeforeDepth.TextureId), ReadDepth(capture.AfterDepth.TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion

    #region Protected API
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion

    #region Private
/// <summary>Establishes caller-owned scissor state before a capture uses its cached preservation contract.</summary>
    private static void EstablishScissor(bool enabled)
    {
        var mask = GlPipelineStateMask.From(GlPipelineStateId.ScissorTestEnable);
        OpenGlStateCache.Current.Apply(new GlPipelineDesc(enabled ? default : mask, enabled ? mask : default));
    }
    /// <summary>Issues one ordered source-alpha particle draw with an independently chosen visibility depth.</summary>
    private static void DrawParticle(PackedUniformBuffer inputs, float red, float green, float alpha, float depth)
    {
        Span<byte> bytes = stackalloc byte[32];
        bytes.Clear();
        UboPacking.WriteVec4(bytes, 0, red, green, 0f, alpha);
        UboPacking.WriteFloat(bytes, 16, depth);
        inputs.SetBytes(bytes);
        Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.ShaderInputs, inputs.Capture()));
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    /// <summary>Reads the actual depth texture representation without tolerance or reconstruction.</summary>
    private float ReadDepth(int texture)
    {
        using var image=new BorrowedTexture(texture);
        float selected=GpuLocalRead.Tuple(image,0,0)[0];
        ImageAssertions.Comparisons.CompareConstant(image,selected).AssertEqual();
        return selected;
    }

    #endregion
}
