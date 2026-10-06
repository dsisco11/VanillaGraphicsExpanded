using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Proves the fullscreen consumer's state footprint with deterministic real MRT drawing.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CompleteGraphicsDrawTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A complete fullscreen description overrides hostile engine state and restores it after drawing or throwing.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FullscreenDrawIsIndependentAndBoundaryRestores(bool fail)
    {
        fixture.MakeCurrent(); using var draw = new FullscreenDraw();
        var cache = StateCache.Current;
        GL.UseProgram(0); GL.Disable(EnableCap.StencilTest); GL.Enable(EnableCap.RasterizerDiscard);
        GL.Enable(EnableCap.ScissorTest); GL.Scissor(0, 0, 0, 0); GL.ColorMask(false, false, false, false);
        GL.Enable(EnableCap.SampleMask); GL.SampleMask(0, 0); GL.BlendEquation(BlendEquationMode.Max);
        GL.DepthRange(.25, .75); cache.InvalidateAll();
        var description = Description(); var expected = new InvalidOperationException("after draw");
        long submissions = cache.DrawSubmissions;
        try
        {
            bool Run() => CompleteGraphicsBoundary.TryRun("FullscreenFixture", [description], [], true, _ =>
            {
                cache.ApplyGraphicsState(description, new GraphicsDynamicState { Viewport = new() { Width = 8, Height = 8 } });
                draw.Render();
                if (fail) throw expected;
            });
            if (fail) Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => Run()));
            else Assert.True(Run());
            Assert.Equal(submissions + 1, cache.DrawSubmissions);
            Assert.True(GL.IsEnabled(EnableCap.RasterizerDiscard)); Assert.True(GL.IsEnabled(EnableCap.ScissorTest));
            Assert.True(GL.IsEnabled(EnableCap.SampleMask));
            GL.GetInteger((GetIndexedPName)All.SampleMaskValue, 0, out int mask); Assert.Equal(0, mask);
            double[] range = new double[2]; GL.GetDouble(GetPName.DepthRange, range); Assert.Equal(new[] { .25, .75 }, range);
            Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
            draw.AssertPixels(); Assert.Equal(ErrorCode.NoError, GL.GetError());
            bool called = false;
            Assert.False(CompleteGraphicsBoundary.TryRun("ExcludedConditional", [description], [], false, _ => called = true));
            Assert.False(called);
        }
        finally { cache.ApplyGraphicsState(description, new GraphicsDynamicState { Viewport = new() { Width = 8, Height = 8 } }); }
    }
    #endregion
    #region Private
    /// <summary>Uses the lighting consumer's three-color fullscreen shape without migrating its production renderer.</summary>
    private static GraphicsPipelineDesc Description()
    {
        var contract = new GpuShaderContract("fullscreen", [new("fullscreen.vsh", "fullscreen.vsh", ShaderStageKind.Vertex, new()), new("fullscreen.fsh", "fullscreen.fsh", ShaderStageKind.Fragment, new())], 1);
        return new(new("fixture", new(new ShaderSettings(contract))), new([]), new([new(PixelInternalFormat.Rgba8), new(PixelInternalFormat.Rgba8), new(PixelInternalFormat.Rgba8)]), DynamicPipelineState.Viewport);
    }

    /// <summary>Owns three output images and an indexed fullscreen triangle using the production draw owner.</summary>
    private sealed class FullscreenDraw : IDisposable
    {
        private readonly int program, framebuffer;
        private readonly int[] colors = new int[3];
        private readonly GpuVao vao;
        private readonly GpuEbo indices;
        #region Public API
        /// <summary>Loads deterministic binaries and creates an independent render target.</summary>
        public FullscreenDraw()
        {
            int vs = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.LoadFixture("tests/complete-state.vsh", ShaderType.VertexShader);
            int fs = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.LoadFixture("tests/complete-state.fsh", ShaderType.FragmentShader);
            program = GL.CreateProgram(); GL.AttachShader(program, vs); GL.AttachShader(program, fs); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked); Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.DeleteShader(vs); GL.DeleteShader(fs);
            vao = GpuVao.Create(); vao.Bind(); indices = GpuEbo.Create(); indices.UploadIndices(new uint[] { 0, 1, 2 });
            framebuffer = GL.GenFramebuffer(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = GL.GenRenderbuffer(); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colors[i]);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, 8, 8);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + i, RenderbufferTarget.Renderbuffer, colors[i]);
            }
            GL.DrawBuffers(3, [DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2]);
            Assert.Equal(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
            StateCache.Current.InvalidateAll();
        }
        /// <summary>Issues one native indexed draw after the caller establishes complete state.</summary>
        public void Render() { using var shader = StateCache.Current.UseProgramScope(program); vao.DrawElements(PrimitiveType.Triangles, indices); }
        /// <summary>Checks all pixels of all outputs, independent of GL state metadata.</summary>
        public void AssertPixels()
        {
            using var pack = StateCache.Current.SetPixelPackScope(new(1));
            for (int target = 0; target < 3; target++)
            {
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0 + target); byte[] pixels = new byte[8 * 8 * 4];
                GL.ReadPixels(0, 0, 8, 8, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                for (int i = 0; i < pixels.Length; i++) Assert.Equal((byte)(i % 4 == target || i % 4 == 3 ? 255 : 0), pixels[i]);
            }
        }
        /// <summary>Releases fixture resources while the collection context remains active.</summary>
        public void Dispose()
        {
            StateCache.Current.UseProgram(0); indices.Dispose(); vao.Dispose(); GL.DeleteProgram(program);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.DeleteFramebuffer(framebuffer);
            foreach (int color in colors) GL.DeleteRenderbuffer(color); StateCache.Current.InvalidateAll();
        }
        #endregion
    }
    #endregion
}
