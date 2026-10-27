using VanillaGraphicsExpanded.Rendering.Pipeline;
using System;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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
public sealed class CompleteGraphicsDrawTests(HeadlessGLFixture fixture) : IDisposable
{
    private readonly GraphicsStateShaders stateShaders = new();
    /// <summary>Retires test-owned executable generations after state assertions complete.</summary>
    public void Dispose() => stateShaders.Dispose();

    #region Public API
    /// <summary>Disabled culling still establishes authored winding because fragments observe gl_FrontFacing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrontFacingIsObservableWithCullingDisabled(bool clockwise)
    {
        fixture.MakeCurrent(); using var draw = new FullscreenDraw(facing: true);
        var cache = OpenGlStateCache.Current; OpenGLProgramSelection.Select(cache, 0);
        var baseline = Description();
        var description = new RHIGraphicsPipelineDescription(baseline.Shader, baseline.VertexLayout, baseline.Targets, baseline.Dynamics,
            rasterizer: new() { Cull = false, FrontFace = clockwise ? FrontFaceWinding.Clockwise : FrontFaceWinding.CounterClockwise });
        GL.FrontFace(clockwise ? FrontFaceDirection.Ccw : FrontFaceDirection.Cw); GL.Enable(EnableCap.CullFace);
        cache.InvalidateAll(); int inherited = GL.GetInteger(GetPName.FrontFace);
        try
        {
            Assert.True(((Func<bool>)(() => {
                if (!true) return false;
                var coverage = PipelineStateCoverage.Empty;
                foreach (RHIGraphicsPipelineDescription declaration in new RHIGraphicsPipelineDescription[] {description})
                    coverage = coverage.Union(PipelineStateCoverage.From(VanillaGraphicsExpanded.Rendering.Pipeline.GraphicsStateRelevance.Derive(declaration, GpuSupport.Graphics)));
                if (!OpenGlStateCache.Current.TryBeginEngineBoundary(new EngineBoundaryDeclaration("FacingWithoutCull", coverage), out var restored,
                    new EngineBoundaryResources(geometry: true, programSelection: true))) return false;
                using (restored) {
                    Action<EngineBoundaryScope> operation = _ =>
            {
                cache.ApplyGraphicsState(description, new() { Viewport = new() { Width = 8, Height = 8 } });
                Assert.False(GL.IsEnabled(EnableCap.CullFace)); draw.Render();
            };
                    restored!.Run(() => operation(restored));
                }
                return true;
            }))());
            Assert.Equal(inherited, GL.GetInteger(GetPName.FrontFace)); Assert.True(GL.IsEnabled(EnableCap.CullFace));
            draw.AssertFacingPixels(front: !clockwise); Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.Disable(EnableCap.CullFace); GL.FrontFace(FrontFaceDirection.Ccw); cache.InvalidateAll(); }
    }
    /// <summary>A complete fullscreen description overrides hostile engine state and restores it after drawing or throwing.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FullscreenDrawIsIndependentAndBoundaryRestores(bool fail)
    {
        fixture.MakeCurrent(); using var draw = new FullscreenDraw();
        var cache = OpenGlStateCache.Current;
        GL.UseProgram(0); GL.Disable(EnableCap.StencilTest); GL.Enable(EnableCap.RasterizerDiscard);
        GL.Enable(EnableCap.ScissorTest); GL.Scissor(0, 0, 0, 0); GL.ColorMask(false, false, false, false);
        GL.Enable(EnableCap.SampleMask); GL.SampleMask(0, 0); GL.BlendEquation(BlendEquationMode.Max);
        GL.DepthRange(.25, .75); cache.InvalidateAll();
        var description = Description(); var expected = new InvalidOperationException("after draw");
#if DEBUG
        long submissions = draw.Submissions;
#endif
        try
        {
            bool Run() => ((Func<bool>)(() => {
                if (!true) return false;
                var coverage = PipelineStateCoverage.Empty;
                foreach (RHIGraphicsPipelineDescription declaration in new RHIGraphicsPipelineDescription[] {description})
                    coverage = coverage.Union(PipelineStateCoverage.From(VanillaGraphicsExpanded.Rendering.Pipeline.GraphicsStateRelevance.Derive(declaration, GpuSupport.Graphics)));
                if (!OpenGlStateCache.Current.TryBeginEngineBoundary(new EngineBoundaryDeclaration("FullscreenFixture", coverage), out var restored,
                    new EngineBoundaryResources(geometry: true, programSelection: true))) return false;
                using (restored) {
                    Action<EngineBoundaryScope> operation = _ =>
            {
                cache.ApplyGraphicsState(description, new RHIDynamicState { Viewport = new() { Width = 8, Height = 8 } });
                draw.Render();
                if (fail) throw expected;
            };
                    restored!.Run(() => operation(restored));
                }
                return true;
            }))();
            if (fail) Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => Run()));
            else Assert.True(Run());
#if DEBUG
            Assert.Equal(submissions + 1, draw.Submissions);
#endif
            Assert.True(GL.IsEnabled(EnableCap.RasterizerDiscard)); Assert.True(GL.IsEnabled(EnableCap.ScissorTest));
            Assert.True(GL.IsEnabled(EnableCap.SampleMask));
            GL.GetInteger((GetIndexedPName)All.SampleMaskValue, 0, out int mask); Assert.Equal(0, mask);
            double[] range = new double[2]; GL.GetDouble(GetPName.DepthRange, range); Assert.Equal(new[] { .25, .75 }, range);
            Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
            draw.AssertPixels(); Assert.Equal(ErrorCode.NoError, GL.GetError());
            bool called = false;
            Assert.False(((Func<bool>)(() => {
                if (!false) return false;
                var coverage = PipelineStateCoverage.Empty;
                foreach (RHIGraphicsPipelineDescription declaration in new RHIGraphicsPipelineDescription[] {description})
                    coverage = coverage.Union(PipelineStateCoverage.From(VanillaGraphicsExpanded.Rendering.Pipeline.GraphicsStateRelevance.Derive(declaration, GpuSupport.Graphics)));
                if (!OpenGlStateCache.Current.TryBeginEngineBoundary(new EngineBoundaryDeclaration("ExcludedConditional", coverage), out var restored,
                    new EngineBoundaryResources(geometry: true, programSelection: true))) return false;
                using (restored) {
                    Action<EngineBoundaryScope> operation = _ => called = true;
                    restored!.Run(() => operation(restored));
                }
                return true;
            }))());
            Assert.False(called);
        }
        finally { cache.ApplyGraphicsState(description, new RHIDynamicState { Viewport = new() { Width = 8, Height = 8 } }); }
    }

    /// <summary>Global fallback uses enabled output intent even when an earlier disabled output retains different factors.</summary>
    [Fact]
    public void GlobalBlendFallbackIgnoresDormantEarlierOutputFactors()
    {
        fixture.MakeCurrent();
        using var draw = new FullscreenDraw();
        var original = GpuSupport.Graphics;
        var capabilityField = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "graphics");
        var cache = OpenGlStateCache.Current;
        try
        {
            // The real context supports global blending; only the optional independent path is disabled.
            capabilityField.SetValue(null, original with { IndependentBlend = false });
            var baseline = Description();
            var description = new RHIGraphicsPipelineDescription(baseline.Shader, baseline.VertexLayout, baseline.Targets,
                baseline.Dynamics, blending: [new() { SourceRgb = BlendFactor.Zero, DestinationRgb = BlendFactor.One },
                    new() { Enabled = true }, new()]);
            GL.Disable(EnableCap.ScissorTest); GL.Disable(EnableCap.RasterizerDiscard); GL.ColorMask(true, true, true, true);
            for (int slot = 0; slot < 3; slot++) GL.ClearBuffer(ClearBuffer.Color, slot, new float[4]);
            GL.BlendFunc(BlendingFactor.Zero, BlendingFactor.One);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            cache.InvalidateAll();
            Assert.True(((Func<bool>)(() => {
                if (!true) return false;
                var coverage = PipelineStateCoverage.Empty;
                foreach (RHIGraphicsPipelineDescription declaration in new RHIGraphicsPipelineDescription[] {description})
                    coverage = coverage.Union(PipelineStateCoverage.From(VanillaGraphicsExpanded.Rendering.Pipeline.GraphicsStateRelevance.Derive(declaration, GpuSupport.Graphics)));
                if (!OpenGlStateCache.Current.TryBeginEngineBoundary(new EngineBoundaryDeclaration("GlobalBlendFallback", coverage), out var restored,
                    new EngineBoundaryResources(geometry: true, programSelection: true))) return false;
                using (restored) {
                    Action<EngineBoundaryScope> operation = _ =>
            {
                cache.ApplyGraphicsState(description, new() { Viewport = new() { Width = 8, Height = 8 } });
                GL.GetInteger((GetIndexedPName)GetPName.BlendSrcRgb, 1, out int source);
                GL.GetInteger((GetIndexedPName)GetPName.BlendDstRgb, 1, out int destination);
                Assert.Equal((int)BlendingFactorSrc.One, source);
                Assert.Equal((int)BlendingFactorDest.Zero, destination);
                draw.Render();
            };
                    restored!.Run(() => operation(restored));
                }
                return true;
            }))());
            draw.AssertPixels();
            for (int slot = 0; slot < cache.MaxDrawBuffers; slot++)
            {
                GL.GetInteger((GetIndexedPName)GetPName.BlendSrcRgb, slot, out int source);
                GL.GetInteger((GetIndexedPName)GetPName.BlendDstRgb, slot, out int destination);
                Assert.Equal((int)BlendingFactorSrc.Zero, source);
                Assert.Equal((int)BlendingFactorDest.One, destination);
                GL.GetInteger((GetIndexedPName)GetPName.BlendSrcAlpha, slot, out int alphaSource);
                GL.GetInteger((GetIndexedPName)GetPName.BlendDstAlpha, slot, out int alphaDestination);
                Assert.Equal((int)BlendingFactorSrc.Zero, alphaSource);
                Assert.Equal((int)BlendingFactorDest.One, alphaDestination);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            capabilityField.SetValue(null, original);
            cache.InvalidateAll();
        }
    }
    #endregion
    #region Private
    /// <summary>Uses the lighting consumer's three-color fullscreen shape without migrating its production renderer.</summary>
    private RHIGraphicsPipelineDescription Description()
    {
        var contract = new GpuShaderContract("fullscreen", [new("fullscreen.vsh", "fullscreen.vsh", ShaderStageKind.Vertex, new()), new("fullscreen.fsh", "fullscreen.fsh", ShaderStageKind.Fragment, new())], 1);
        return new(stateShaders.Get(contract.Stages.Select(stage => stage.Kind)), new([]), new([new(TextureFormat.Rgba8), new(TextureFormat.Rgba8), new(TextureFormat.Rgba8)]), DynamicPipelineState.Viewport);
    }

    /// <summary>Owns three output images and an indexed fullscreen triangle using the production draw owner.</summary>
    private sealed class FullscreenDraw : IDisposable
    {
        private readonly int program, framebuffer;
        private readonly int[] colors = new int[3];
        private readonly GpuVao vao;
        private readonly GpuEbo indices;
        /// <summary>Counts explicit native reference draws independently of production submission instrumentation.</summary>
        internal int Submissions { get; private set; }
        #region Public API
        /// <summary>Loads deterministic binaries and creates an independent render target.</summary>
        public FullscreenDraw(bool facing = false)
        {
            int vs = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.LoadFixture("tests/complete-state.vsh", ShaderType.VertexShader);
            int fs = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.LoadFixture(
                facing ? "tests/facing-state.fsh" : "tests/complete-state.fsh", ShaderType.FragmentShader);
            program = GL.CreateProgram(); GL.AttachShader(program, vs); GL.AttachShader(program, fs); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked); Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.DeleteShader(vs); GL.DeleteShader(fs);
            vao = GpuVao.Create(); OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId); indices = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); indices.UploadIndices(new uint[] { 0, 1, 2 });
            framebuffer = GL.GenFramebuffer(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = GL.GenRenderbuffer(); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colors[i]);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, 8, 8);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + i, RenderbufferTarget.Renderbuffer, colors[i]);
            }
            GL.DrawBuffers(3, [DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2]); OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
            Assert.Equal(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
            OpenGlStateCache.Current.InvalidateAll();
        }
        /// <summary>Issues one native indexed draw after the caller establishes complete state.</summary>
        public void Render()
        {
            int previous = GL.GetInteger(GetPName.CurrentProgram);
            using var selection = OpenGlStateCache.Current.BindVertexArrayScope(vao.VertexArrayId);
            try
            {
                OpenGLProgramSelection.Select(OpenGlStateCache.Current, program);
                OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(vao.VertexArrayId, indices.BufferId);
                GL.DrawElements(PrimitiveType.Triangles, 3, DrawElementsType.UnsignedInt, IntPtr.Zero);
                Submissions++;
            }
            finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, previous); }
        }
        /// <summary>Checks all pixels of all outputs, independent of GL state metadata.</summary>
        public void AssertPixels()
        {
            using var pack = OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new(1));
            for (int target = 0; target < 3; target++)
            {
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0 + target); byte[] pixels = new byte[8 * 8 * 4];
                GL.ReadPixels(0, 0, 8, 8, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                for (int i = 0; i < pixels.Length; i++) Assert.Equal((byte)(i % 4 == target || i % 4 == 3 ? 255 : 0), pixels[i]);
            }
        }
        /// <summary>Checks shader-observed facing for every covered pixel independently of native winding metadata.</summary>
        public void AssertFacingPixels(bool front)
        {
            using var pack = OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new(1));
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0); byte[] pixels = new byte[8 * 8 * 4];
            GL.ReadPixels(0, 0, 8, 8, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            for (int pixel = 0; pixel < 64; pixel++)
                Assert.Equal(front ? new byte[] { 255, 0, 0, 255 } : new byte[] { 0, 255, 0, 255 }, pixels.AsSpan(pixel * 4, 4).ToArray());
        }
        /// <summary>Releases fixture resources while the collection context remains active.</summary>
        public void Dispose()
        {
            OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); indices.Dispose(); vao.Dispose(); GL.DeleteProgram(program);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.DeleteFramebuffer(framebuffer);
            foreach (int color in colors) GL.DeleteRenderbuffer(color); OpenGlStateCache.Current.InvalidateAll();
        }
        #endregion
    }
    #endregion
}
