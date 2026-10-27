using System;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using ErrorCode = OpenTK.Graphics.OpenGL.ErrorCode;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises native configurable raster transitions in core and compatibility contexts.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ConfigurableRasterizerGpuTests(HeadlessGLFixture fixture, ITestOutputHelper output) : IDisposable
{
    private readonly GraphicsStateShaders stateShaders = new();
    /// <summary>Retires test-owned executable generations after state assertions complete.</summary>
    public void Dispose() => stateShaders.Dispose();

    #region Public API
    /// <summary>Core-supported parameters agree with GL, suppress repeats, and restore scoped mutations.</summary>
    [Fact]
    public void CoreTransitionsRestoreAndSuppressRepeats()
    {
        fixture.MakeCurrent();
        Assert.Equal(GL.GetInteger(GetPName.MaxClipDistances), GpuSupport.Graphics.MaxClipDistances);
        output.WriteLine($"Raster core: {GL.GetString(StringName.Version)}; {GL.GetString(StringName.Renderer)}; ClipControl={GpuSupport.Graphics.ClipControl}");
        var cache = OpenGlStateCache.Current; cache.InvalidateAll();
        var first = Pipeline(new() { LineSmooth = true, PolygonSmooth = true,
            PointSpriteOrigin = PointSpriteOrigin.LowerLeft });
        var second = Pipeline(new());
        cache.ApplyConfigurableRaster(first);
        try
        {
            EngineStateCalls.Enable(EnableCap.ClipDistance1);
            cache.SetClipDistance(0, false); Assert.True(GL.IsEnabled(EnableCap.ClipDistance1));
            cache.SetClipDistance(0, true);
            Assert.True(GL.IsEnabled(EnableCap.ClipDistance0));
            Assert.True(GL.IsEnabled(EnableCap.LineSmooth));
            Assert.True(GL.IsEnabled(EnableCap.PolygonSmooth));
            cache.SetClipDistance(1, false);
            cache.ApplyConfigurableRaster(first);
            Assert.False(GL.IsEnabled(EnableCap.ClipDistance0));
#if DEBUG
            long calls = cache.FixedFunctionCalls;
#endif
            cache.ApplyConfigurableRaster(first);
#if DEBUG
            Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
            cache.ApplyConfigurableRaster(second);
            cache.ApplyConfigurableRaster(first);
            Assert.False(GL.IsEnabled(EnableCap.ClipDistance0));
            EngineStateCalls.Enable(EnableCap.ClipDistance0);
            cache.SetDepthFunc(DepthFunction.Less);
#if DEBUG
            calls = cache.FixedFunctionCalls;
#endif
            cache.Invalidate(EPipelineState.ConfigurableRaster);
            cache.SetDepthFunc(DepthFunction.Less);
#if DEBUG
            Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
            Assert.True(cache.TryBeginEngineBoundary(Declaration(), out var scope));
            var failure = new InvalidOperationException("fixture");
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => scope!.Run(() => {
                cache.ApplyConfigurableRaster(second); cache.SetClipDistance(1, true); throw failure;
            })));
            Assert.True(GL.IsEnabled(EnableCap.ClipDistance0));
            Assert.True(GL.IsEnabled(EnableCap.PolygonSmooth)); Assert.False(GL.IsEnabled(EnableCap.ClipDistance1));
            Assert.True(cache.TryBeginEngineBoundary(Declaration(), out var successful));
            successful!.Run(() => {
                cache.ApplyConfigurableRaster(second);
                Assert.False(GL.IsEnabled(EnableCap.ClipDistance0));
            });
            Assert.True(GL.IsEnabled(EnableCap.ClipDistance0));
            Assert.Equal((int)PointSpriteCoordOriginParameter.LowerLeft, GL.GetInteger((GetPName)All.PointSpriteCoordOrigin));
            GlDebug.CheckStateTransitions = true;
            GL.Enable((EnableCap)(-1));
            Assert.Throws<InvalidOperationException>(() => cache.SetLineSmooth(false));
#if DEBUG
            calls = cache.FixedFunctionCalls;
#endif
            cache.SetLineSmooth(false);
#if DEBUG
            Assert.Equal(calls + 1, cache.FixedFunctionCalls);
#endif
            Assert.False(GL.IsEnabled(EnableCap.LineSmooth));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GlDebug.CheckStateTransitions = false; cache.ApplyConfigurableRaster(second); }
    }

    /// <summary>Real texture uploads preserve native and cached unpack layout while publishing the requested pixels.</summary>
    [Fact]
    public void TextureUploadPreservesUnpackKnowledge()
    {
        using var context = new CompatibilityContext(fixture, output);
        var cache = OpenGlStateCache.Current;
        cache.InvalidateAll();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R16, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        OpenGLPixelLayouts.SetUnpack(cache, new(8));
        texture.UploadDataImmediate(new ushort[] { 17 });
        Assert.Equal(8, GL.GetInteger(GetPName.UnpackAlignment));
        Assert.Equal(8, OpenGLPixelLayouts.GetUnpack(cache).Alignment);
        Assert.Equal(17f / ushort.MaxValue, texture.ReadPixels()[0], precision: 6);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Real fragments exercise matching clip-depth conventions.</summary>
    [Fact]
    public void ClipDepthPoliciesChangePixels()
    {
        using var context = new CompatibilityContext(fixture, output);
        using var draw = new RasterDraw();
        var cache = OpenGlStateCache.Current;
        cache.InvalidateAll();
        Assert.Equal(1024, draw.Count(Pipeline(new())));
        Assert.True(draw.Count(Pipeline(new()), lines: true) > 0);
        if (GpuSupport.Graphics.ClipControl)
        {
            Assert.Equal(0, draw.Count(Pipeline(new() { ClipOrigin = ClipCoordinateOrigin.UpperLeft, ClipDepth = ClipDepthRange.ZeroToOne }), zeroToOne: false));
            Assert.Equal(1024, draw.Count(Pipeline(new() { ClipOrigin = ClipCoordinateOrigin.UpperLeft, ClipDepth = ClipDepthRange.ZeroToOne }), zeroToOne: true));
            Assert.Equal(255, draw.LastPixels[(4 * 32 + 16) * 4 + 1]);
            Assert.Equal(1024, draw.Count(Pipeline(new()), zeroToOne: false));
            Assert.Equal(0, draw.LastPixels[(4 * 32 + 16) * 4 + 1]);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Declares the entire configurable raster boundary.</summary>
    private static EngineBoundaryDeclaration Declaration() => new("RasterFixture", new PipelineStateCoverage(rasterizer: RasterizerStateKnowledge.ConfigurableRaster));
    /// <summary>Builds a real-capability descriptor without duplicating shader output metadata.</summary>
    private RHIGraphicsPipelineDescription Pipeline(RasterizerDesc raster)
    {
        var contract = new GpuShaderContract("raster", [new("raster.vsh", "raster.vsh", ShaderStageKind.Vertex, new()),
            new("raster.fsh", "raster.fsh", ShaderStageKind.Fragment, new())], 1);
        return new(stateShaders.Get(contract.Stages.Select(stage => stage.Kind)), new([]), new([new(TextureFormat.Rgba8)]), DynamicPipelineState.Viewport,
            rasterizer: raster);
    }

    /// <summary>Owns a temporary compatibility context and restores the collection context afterward.</summary>
    private sealed unsafe class CompatibilityContext : IDisposable
    {
        private readonly HeadlessGLFixture fixture;
        private Window* window;
        /// <summary>Creates an independently registered hidden compatibility context.</summary>
        public CompatibilityContext(HeadlessGLFixture fixture, ITestOutputHelper output)
        {
            this.fixture = fixture; fixture.MakeCurrent();
            GLFW.DefaultWindowHints();
            GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4); GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 5);
            GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Compat);
            GLFW.WindowHint(WindowHintBool.Visible, false);
            window = GLFW.CreateWindow(32, 32, "Raster compatibility test", null, null);
            Assert.SkipWhen(window == null, "Compatibility GL 4.3 context unavailable.");
            GLFW.MakeContextCurrent(window); GL.LoadBindings(new GLFWBindingsContext());
            RenderContextRegistry.RegisterCurrent(this, owner => ((CompatibilityContext)owner).window != null);
            GpuSupport.EnsureCurrentContext(); OpenGlStateCache.Current.InvalidateAll();
            Assert.False(GpuSupport.Graphics.CoreProfile);
            output.WriteLine($"Raster compatibility: {GL.GetString(StringName.Version)}; {GL.GetString(StringName.Renderer)}; ClipControl={GpuSupport.Graphics.ClipControl}");
        }
        /// <summary>Retires only this fixture lifetime and makes the original fixture current.</summary>
        public void Dispose()
        {
            RenderContextRegistry.Retire(this); GLFW.DestroyWindow(window); window = null;
            fixture.MakeCurrent(); GpuSupport.EnsureCurrentContext(); OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Owns deterministic raw test geometry, shader, target and readback resources.</summary>
    private sealed class RasterDraw : IDisposable
    {
        private readonly int program, vao, framebuffer, color;
        private readonly PackedUniformBuffer inputs = new(16);
        private readonly GpuUniformRingFrameController frame;
        private readonly GpuUniformRingBuffer? previousRing;
        public byte[] LastPixels { get; private set; } = [];
        /// <summary>Creates a 32-square target and vertex-ID geometry for compatibility raster checks.</summary>
        public RasterDraw()
        {
            int vertex = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.LoadFixture("tests/rasterizer.vsh", ShaderType.VertexShader);
            int fragment = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.LoadFixture("tests/rasterizer.fsh", ShaderType.FragmentShader);
            program = GL.CreateProgram(); GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked); Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.DeleteShader(vertex); GL.DeleteShader(fragment);
            vao = GL.GenVertexArray(); framebuffer = GL.GenFramebuffer(); color = GL.GenRenderbuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, color);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, 32, 32);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, color);
            Assert.Equal(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
            GL.Viewport(0, 0, 32, 32); GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            // This fixture also runs in an isolated compatibility context. Its publications
            // must retire on that context rather than borrowing the primary fixture's ring.
            GpuUniformRingSystem.TryGetCurrent(out previousRing);
            frame = new(new GpuUniformRingBuffer(4096, 1));
            frame.BeginFrame();
        }
        /// <summary>Applies production raster policy, draws and counts nonzero red pixels.</summary>
        public int Count(RHIGraphicsPipelineDescription pipeline, bool lines = false, bool zeroToOne = false)
        {
            OpenGlStateCache.Current.ApplyConfigurableRaster(pipeline);
            GL.ClearColor(0, 0, 0, 0); GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.UseProgram(program); GL.BindVertexArray(vao);
            Span<byte> bytes = stackalloc byte[16];
            bytes.Clear();
            UboPacking.WriteInt32(bytes, 0, lines ? 1 : 0);
            UboPacking.WriteInt32(bytes, 4, zeroToOne ? 1 : 0);
            inputs.SetBytes(bytes);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.ShaderInputs, inputs.Capture()));
            GL.DrawArrays(lines ? PrimitiveType.Lines : PrimitiveType.Triangles, 0, lines ? 2 : 3);
            byte[] pixels = new byte[32 * 32 * 4]; GL.ReadPixels(0, 0, 32, 32, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            LastPixels = pixels;
            return Enumerable.Range(0, 1024).Count(i => pixels[i * 4] != 0);
        }
        /// <summary>Deletes native fixture resources before their context is destroyed.</summary>
        public void Dispose()
        {
            inputs.Dispose();
            try { frame.EndFrame(); }
            finally
            {
                frame.Dispose();
                if (previousRing != null) GpuUniformRingSystem.SetCurrent(previousRing);
                GL.UseProgram(0); GL.DeleteProgram(program); GL.DeleteVertexArray(vao); GL.DeleteFramebuffer(framebuffer); GL.DeleteRenderbuffer(color);
            }
        }
    }
    #endregion
}
