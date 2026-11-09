using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Integration.Support.Rendering.Shaders;

using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Postprocessing;

/// <summary>Exercises isolated particle blending, depth separation and borrowed image lifetimes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SceneColorParticleTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API

    /// <summary>Equal-sized primary publication invalidates captures and disposal retires only owned snapshots.</summary>
    [Fact]
    public void PrimaryRepublicationInvalidatesAndDisposalKeepsBorrowedImages()
    {
        // Arrange: Create primary borrowed attachments and a capture owner that records only its own snapshots.
        const TextureFormat depthFormat = TextureFormat.DepthComponent32;
        EnsureShaderTestAvailable();
        EstablishScissor(false);
        using var material = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba16f);
        using var glow = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba8);
        using var depthStorage = new DepthTexture(2, 2, depthFormat);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var engine = DynamicGpuFramebuffer.Create([material, glow], depth);
        using var primary = DynamicGpuFramebuffer.Wrap(engine.FboId, width: 2, height: 2);
        using var capture = new SceneColorParticleTargets(primary);
        int[] owned = [capture.ParticleColor.TextureId, capture.BeforeDepth.TextureId, capture.AfterDepth.TextureId,
            capture.MaterialDepth.TextureId, capture.VisibleRadiance.TextureId];
        // Act: Capture once, republish the wrapped framebuffer, then dispose and drain deferred cleanup.
        capture.BeginCapture();
        capture.EndCapture();
        primary.RefreshWrappedFramebuffer(engine.FboId, 2, 2);
        Assert.False(capture.IsCurrent);
        Assert.False(capture.Captured);
        Assert.Throws<InvalidOperationException>(capture.BeginCapture);
        capture.Dispose();
        RHIResourceManagerSystem.CaptureDisposalQueue().DrainPending();
        // Assert: Owned snapshots are deleted while all borrowed primary attachments stay live.
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
        // Arrange: Build the installed cube-particle fragment with authored color, glow, and alpha inputs.
        EnsureShaderTestAvailable();
        EstablishScissor(false);
        string vertex = """
            #version 430 core
            layout(location=0) in vec2 position;
            out vec4 color; out vec2 uv; out float glowLevel; out float fogAmount;
            out vec4 rgbaFog; out vec3 normal; out vec4 worldPos; out vec4 fragPosition; out vec4 gnormal;
            void main(){gl_Position=vec4(position,0,1);color=vec4(.5,.25,.75,.4);uv=vec2(0);glowLevel=.2;fogAmount=0;rgbaFog=vec4(0);normal=vec3(0,1,0);worldPos=vec4(0);fragPosition=vec4(10,20,30,1);gnormal=vec4(.25,.5,.75,1);}
            """;
        string fragment = InstalledEngineShaderSources.Build("particlescube.fsh", 0, 0, ssao, 0, 0);
        using var program = GpuProgramObject.Adopt(TestShaderCache.CreateGraphics(vertex, fragment));
        using var lighting = new TestFrameLightingPublication();
        lighting.Bind();
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), linearScene);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "cameraUnderwater"), 1f);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "shadowIntensity"), 0f);
        GL.ProgramUniform3(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "lightPosition"), 0f, 1f, 0f);
        using var target = CreateMRTRenderTarget(1, 1, Enumerable.Repeat(TextureFormat.Rgba32f, ssao > 0 ? 4 : 2).ToArray());
        // Act: Render the installed variant and read primary plus optional SSAO outputs.
        TestFramework.RenderNativeReferenceQuadTo(program.ProgramId, target);
        float[] actual = target[0].ReadPixels();
        // Assert: Scene convention changes only RGB conversion; glow, alpha, and SSAO captures remain authored.
        float[] authored = [.5f, .25f, .75f];
        for (int channel = 0; channel < 3; channel++)
        {
            float expected = linearScene == 0 ? authored[channel] : 32 * MathF.Pow((authored[channel] + .055f) / 1.055f, 2.4f);
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
        // Arrange: Seed independent read/draw framebuffers and nondefault indexed masks and blending.
        EnsureShaderTestAvailable();
        fixture.InitializeResourceDisposal();
        EstablishScissor(false);
        using var material = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba16f);
        using var glow = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba8);
        using var depthStorage = new DepthTexture(2, 2, TextureFormat.DepthComponent32);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var primary = DynamicGpuFramebuffer.Create([material, glow], depth);
        using var other = CreateRenderTarget(2, 2, TextureFormat.Rgba16f);
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
            OpenGlStateCache.Current.InvalidateAll();
            EstablishScissor(true);
            // Act: Run capture entry and exit against the same hostile caller state.
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
            // Assert: Both capture transitions preserve the caller's separate read/draw bindings and indexed state.
        }
        finally
        {
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.Blend);
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            OpenGlStateCache.Current.InvalidateAll();
            EstablishScissor(false);
        }
    }
    /// <summary>Unsupported depth precision rejects setup without retiring any engine-owned attachment.</summary>
    [Fact]
    public void UnsupportedDepthRejectsCaptureWithoutDeletingPrimaryImages()
    {
        // Arrange: Build an otherwise valid primary target with unsupported 24-bit depth storage.
        EnsureShaderTestAvailable();
        EstablishScissor(false);
        using var material = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba16f);
        using var glow = new GpuFramebufferAttachment(2, 2, TextureFormat.Rgba8);
        using var depthStorage = new DepthTexture(2, 2, TextureFormat.DepthComponent24);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var primary = DynamicGpuFramebuffer.Create([material, glow], depth);
        // Act: Attempt to create the capture owner over that borrowed engine target.
        Assert.Throws<InvalidOperationException>(() => new SceneColorParticleTargets(primary));
        // Assert: Rejection leaves all caller-owned images and the framebuffer intact.
        Assert.True(GL.IsTexture(material.TextureId));
        Assert.True(GL.IsTexture(glow.TextureId));
        Assert.True(GL.IsTexture(depth.TextureId));
        Assert.True(primary.CheckStatus(out string? error), error);
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
