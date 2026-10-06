using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises isolated particle blending, depth separation and borrowed image lifetimes.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorParticleTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Ordered radiance survives without overwriting material data; later opaque depth owns replacement pixels.</summary>
    [Theory]
    [InlineData(0, PixelInternalFormat.DepthComponent32)]
    [InlineData(0, PixelInternalFormat.DepthComponent32f)]
    [InlineData(1, PixelInternalFormat.DepthComponent32)]
    [InlineData(2, PixelInternalFormat.DepthComponent32)]
    [InlineData(3, PixelInternalFormat.DepthComponent32)]
    public void CaptureAndResolveSeparateMaterialDepthAndParticleRadiance(int scenario, PixelInternalFormat depthFormat)
    {
        EnsureShaderTestAvailable();
        EstablishScissor(false);
        using var material = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba32f);
        using var glow = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba32f);
        using var depthStorage = new DepthTexture(2, 2, depthFormat);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var primary = GpuFramebuffer.Create([material, glow], depth);
        primary.BindWithViewport();
        GL.Disable(EnableCap.ScissorTest);
        GL.DepthMask(true);
        GL.ColorMask(true, true, true, true);
        GL.ClearBuffer(ClearBuffer.Color, 0, new[] { .125f, .25f, .5f, 1f });
        GL.ClearBuffer(ClearBuffer.Color, 1, new[] { .1f, .2f, .3f, .4f });
        GL.ClearBuffer(ClearBuffer.Depth, 0, new[] { .75f });
        StateCache.Current.InvalidateAll();
        EstablishScissor(false);
        using var capture = new SceneColorParticleTargets(primary);
        Assert.Equal(depth.TextureId, capture.VisibilityDepth);
        Assert.False(capture.Captured);
        capture.BeginCapture();
        Assert.Equal(.75f, ReadDepth(capture.BeforeDepth));
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Load(ShaderType.VertexShader, "tests/complete-state.vsh");
        int fragment = shaders.Load(ShaderType.FragmentShader, "tests/particle-draw.fsh");
        using var draw = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        capture.DrawTarget.BindWithViewport();
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Less);
        GL.DepthMask(true);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);
        StateCache.Current.Apply(SceneColorParticleDrawScope.CapturePipeline);
        GL.UseProgram(draw.ProgramId);
        GL.BindVertexArray(vao.VertexArrayId);
        if (scenario != 3)
        {
            DrawParticle(draw.ProgramId, 8, 0, scenario == 2 ? 0 : .5f, .625f);
            DrawParticle(draw.ProgramId, 0, 4, scenario == 2 ? 0 : .5f, .5f);
        }
        StateCache.Current.InvalidateAll();
        EstablishScissor(false);
        capture.EndCapture();
        Assert.True(capture.Captured);
        Assert.Equal(.75f, ReadDepth(capture.BeforeDepth));
        float visibilityAfterDraw = ReadDepth(depth.TextureId);
        // Fixed-point depth32 conversion can round the nominal fragment value by one float ULP.
        // The actual before/after copies must nevertheless match the source representation exactly.
        Assert.InRange(visibilityAfterDraw, (scenario == 3 ? .75f : .5f) - .0000001f,
            (scenario == 3 ? .75f : .5f) + .0000001f);
        Assert.Equal(visibilityAfterDraw, ReadDepth(capture.AfterDepth));
        float[] captured = capture.DrawTarget[0].ReadPixels();
        float[] expected = scenario >= 2 ? [0, 0, 0, 0] : [2, 2, 0, .75f];
        Assert.Equal(expected, captured[..4]);
        Assert.Equal(new[] { .125f, .25f, .5f, 1f }, primary[0].ReadPixels()[..4]);
        Assert.Equal(scenario == 3 ? new[] { .1f, .2f, .3f, .4f } : new[] { .8f, .7f, .6f, .5f }, primary[1].ReadPixels()[..4]);
        if (scenario == 1)
        {
            primary.Bind();
            GL.DepthMask(true);
            GL.ClearBuffer(ClearBuffer.Depth, 0, new[] { .25f });
            StateCache.Current.InvalidateAll();
            EstablishScissor(false);
        }
        var resolve = Programs.Create<SceneColorParticleShaderProgram>();
        resolve.VisibilityDepth = capture.VisibilityDepth;
        resolve.BeforeDepth = capture.BeforeDepth;
        resolve.AfterDepth = capture.AfterDepth;
        resolve.ParticleColor = capture.ParticleColor;
        TestFramework.RenderQuadTo(resolve, capture.ResolveTarget);
        Assert.Equal(scenario == 1 ? ReadDepth(depth.TextureId) : ReadDepth(capture.BeforeDepth), capture.ResolveTarget[0].ReadPixels()[0]);
        Assert.Equal(scenario == 1 ? new float[4] : expected, capture.ResolveTarget[1].ReadPixels()[..4]);
        Assert.InRange(ReadDepth(depth.TextureId), (scenario == 1 ? .25f : visibilityAfterDraw) - .0000001f, (scenario == 1 ? .25f : visibilityAfterDraw) + .0000001f);
        capture.ResetCapture();
        Assert.False(capture.Captured);
        Assert.Throws<InvalidOperationException>(capture.EndCapture);
        int retainedColor = capture.ParticleColor;
        capture.BeginCapture();
        capture.EndCapture();
        Assert.Equal(retainedColor, capture.ParticleColor);
        Assert.All(capture.DrawTarget[0].ReadPixels(), value => Assert.Equal(0, value));
        Assert.Equal(ReadDepth(capture.BeforeDepth), ReadDepth(capture.AfterDepth));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Equal-sized primary publication invalidates captures and disposal retires only owned snapshots.</summary>
    [Fact]
    public void PrimaryRepublicationInvalidatesAndDisposalKeepsBorrowedImages()
    {
        const PixelInternalFormat depthFormat = PixelInternalFormat.DepthComponent32;
        EnsureShaderTestAvailable();
        EstablishScissor(false);
        using var material = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        using var glow = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba8);
        using var depthStorage = new DepthTexture(2, 2, depthFormat);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var engine = GpuFramebuffer.Create([material, glow], depth);
        using var primary = GpuFramebuffer.Wrap(engine.FboId, width: 2, height: 2);
        using var capture = new SceneColorParticleTargets(primary);
        int[] owned = [capture.ParticleColor, capture.BeforeDepth, capture.AfterDepth,
            capture.ResolveTarget.GetColorTextureId(0), capture.ResolveTarget.GetColorTextureId(1)];
        capture.BeginCapture();
        capture.EndCapture();
        primary.RefreshWrappedFramebuffer(engine.FboId, 2, 2);
        Assert.False(capture.IsCurrent);
        Assert.False(capture.Captured);
        Assert.Throws<InvalidOperationException>(capture.BeginCapture);
        capture.Dispose();
        GpuResourceManagerSystem.CaptureDisposalQueue().DrainPending();
        Assert.All(owned, texture => Assert.False(GL.IsTexture(texture)));
        Assert.True(GL.IsTexture(material.TextureId));
        Assert.True(GL.IsTexture(glow.TextureId));
        Assert.True(GL.IsTexture(depth.TextureId));
        Assert.Throws<ObjectDisposedException>(capture.BeginCapture);
    }
    /// <summary>The complete installed cube-particle fragment decodes scene RGB while retaining glow and alpha.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void InstalledCubeParticleOutputSelectsColorConvention(int linearScene, int ssao)
    {
        EnsureShaderTestAvailable();
        EstablishScissor(false);
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            layout(location=0) in vec2 position;
            out vec4 color; out vec2 uv; out float glowLevel; out float fogAmount;
            out vec4 rgbaFog; out vec3 normal; out vec4 worldPos; out vec4 fragPosition; out vec4 gnormal;
            void main(){gl_Position=vec4(position,0,1);color=vec4(.5,.25,.75,.4);uv=vec2(0);glowLevel=.2;fogAmount=0;rgbaFog=vec4(0);normal=vec3(0,1,0);worldPos=vec4(0);fragPosition=vec4(10,20,30,1);gnormal=vec4(.25,.5,.75,1);}
            """);
        int fragment = shaders.Compile(ShaderType.FragmentShader,
            PbrSurfaceInstalledShaderTests.Build("particlescube.fsh", 0, 0, ssao, 0, 0));
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), linearScene);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "cameraUnderwater"), 1f);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "shadowIntensity"), 0f);
        GL.ProgramUniform3(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "lightPosition"), 0f, 1f, 0f);
        using var target = CreateMRTRenderTarget(1, 1, Enumerable.Repeat(PixelInternalFormat.Rgba32f, ssao > 0 ? 4 : 2).ToArray());
        TestFramework.RenderQuadTo(program.ProgramId, target);
        float[] actual = target[0].ReadPixels();
        float[] authored = [.5f, .25f, .75f];
        for (int channel = 0; channel < 3; channel++)
        {
            float expected = linearScene == 0 ? authored[channel] : MathF.Pow((authored[channel] + .055f) / 1.055f, 2.4f);
            Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
        }
        Assert.Equal(.4f, actual[3]);
        Assert.Equal(new[] { .2f, 0, 0, .4f }, target[1].ReadPixels());
        if (ssao > 0)
        {
            Assert.Equal(new[] { .25f, .5f, .75f, .4f }, target[2].ReadPixels());
            Assert.Equal(new[] { 10f, 20f, 30f, .2f }, target[3].ReadPixels());
        }
    }
    /// <summary>Snapshot copies preserve independent framebuffer bindings, indexed blending and all write masks.</summary>
    [Fact]
    public void CapturePreservesCallerFramebufferAndIndexedState()
    {
        EnsureShaderTestAvailable();
        EstablishScissor(false);
        using var material = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        using var glow = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba8);
        using var depthStorage = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent32);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var primary = GpuFramebuffer.Create([material, glow], depth);
        using var other = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba16f);
        using var capture = new SceneColorParticleTargets(primary);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, primary.FboId);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, other.FboId);
            EstablishScissor(true);
            GL.Scissor(0, 0, 1, 1);
            GL.DepthMask(false);
            GL.ColorMask(0, true, false, true, false);
            GL.ColorMask(1, false, true, false, true);
            GL.Enable(IndexedEnableCap.Blend, 0);
            GL.Disable(IndexedEnableCap.Blend, 1);
            GL.BlendFuncSeparate(0, BlendingFactorSrc.One, BlendingFactorDest.One,
                BlendingFactorSrc.Zero, BlendingFactorDest.OneMinusSrcAlpha);
            StateCache.Current.InvalidateAll();
            EstablishScissor(true);
            foreach (Action operation in new Action[] { capture.BeginCapture, capture.EndCapture })
            {
                operation();
                Assert.Equal(primary.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
                Assert.Equal(other.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
                Assert.True(GL.IsEnabled(EnableCap.ScissorTest));
                Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
                Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, 0));
                Assert.False(GL.IsEnabled(IndexedEnableCap.Blend, 1));
                int[] factors = new int[1];
                GL.GetInteger((GetIndexedPName)GetPName.BlendSrcRgb, 0, factors);
                Assert.Equal((int)BlendingFactorSrc.One, factors[0]);
                GL.GetInteger((GetIndexedPName)GetPName.BlendDstRgb, 0, factors);
                Assert.Equal((int)BlendingFactorDest.One, factors[0]);
                bool[] mask = new bool[4];
                GL.GetBoolean(GetIndexedPName.ColorWritemask, 0, mask);
                Assert.Equal(new[] { true, false, true, false }, mask);
                GL.GetBoolean(GetIndexedPName.ColorWritemask, 1, mask);
                Assert.Equal(new[] { false, true, false, true }, mask);
            }
        }
        finally
        {
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.Blend);
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            StateCache.Current.InvalidateAll();
            EstablishScissor(false);
        }
    }
    /// <summary>Unsupported depth precision rejects setup without retiring any engine-owned attachment.</summary>
    [Fact]
    public void UnsupportedDepthRejectsCaptureWithoutDeletingPrimaryImages()
    {
        EnsureShaderTestAvailable();
        EstablishScissor(false);
        using var material = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        using var glow = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba8);
        using var depthStorage = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent24);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var primary = GpuFramebuffer.Create([material, glow], depth);
        Assert.Throws<InvalidOperationException>(() => new SceneColorParticleTargets(primary));
        Assert.True(GL.IsTexture(material.TextureId));
        Assert.True(GL.IsTexture(glow.TextureId));
        Assert.True(GL.IsTexture(depth.TextureId));
        Assert.True(primary.CheckStatus(out string? error), error);
    }
    #endregion

    #region Private
    /// <summary>Establishes caller-owned scissor state before a capture uses its cached preservation contract.</summary>
    private static void EstablishScissor(bool enabled)
    {
        var mask = GlPipelineStateMask.From(GlPipelineStateId.ScissorTestEnable);
        StateCache.Current.Apply(new GlPipelineDesc(enabled ? default : mask, enabled ? mask : default));
    }
    /// <summary>Issues one ordered source-alpha particle draw with an independently chosen visibility depth.</summary>
    private static void DrawParticle(int program, float red, float green, float alpha, float depth)
    {
        GL.Uniform4(0, red, green, 0f, alpha);
        GL.Uniform1(1, depth);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    /// <summary>Reads the actual depth texture representation without tolerance or reconstruction.</summary>
    private static float ReadDepth(int texture)
    {
        using var binding = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, texture);
        float[] data = new float[4];
        GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.DepthComponent, PixelType.Float, data);
        Assert.All(data, value => Assert.Equal(data[0], value));
        return data[0];
    }
    #endregion
}
