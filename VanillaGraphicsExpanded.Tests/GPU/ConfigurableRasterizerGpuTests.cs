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
public sealed class ConfigurableRasterizerGpuTests(HeadlessGLFixture fixture, ITestOutputHelper output)
{
    #region Public API
    /// <summary>Core-supported parameters agree with GL, suppress repeats, and restore scoped mutations.</summary>
    [Fact]
    public void CoreTransitionsRestoreAndSuppressRepeats()
    {
        fixture.MakeCurrent();
        Assert.Equal(GL.GetInteger(GetPName.MaxClipDistances), GpuSupport.Graphics.MaxClipDistances);
        output.WriteLine($"Raster core: {GL.GetString(StringName.Version)}; {GL.GetString(StringName.Renderer)}; ClipControl={GpuSupport.Graphics.ClipControl}");
        var cache = StateCache.Current;
        cache.InvalidateAll();
        var first = Pipeline(new() { ClipDistances = 1, LineSmooth = true, PolygonSmooth = true,
            PointSpriteOrigin = PointSpriteCoordOriginParameter.LowerLeft });
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
            long calls = cache.FixedFunctionCalls;
            cache.ApplyConfigurableRaster(first);
            Assert.Equal(calls, cache.FixedFunctionCalls);
            cache.ApplyConfigurableRaster(second);
            cache.ApplyConfigurableRaster(first);
            Assert.True(GL.IsEnabled(EnableCap.ClipDistance0));
            cache.SetDepthFunc(DepthFunction.Less);
            calls = cache.FixedFunctionCalls;
            cache.Invalidate(EPipelineState.ConfigurableRaster);
            cache.SetDepthFunc(DepthFunction.Less);
            Assert.Equal(calls, cache.FixedFunctionCalls);
            Assert.True(cache.TryBeginEngineBoundary(Declaration(), out var scope));
            Assert.Equal(AlphaFunction.Always, scope!.Snapshot.Rasterizer.AlphaComparison);
            Assert.Equal(RasterizerDesc.FullPolygonStipple, scope.Snapshot.Rasterizer.PolygonStipplePattern);
            var failure = new InvalidOperationException("fixture");
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => scope!.Run(() => {
                cache.ApplyConfigurableRaster(second); cache.SetClipDistance(1, true); throw failure;
            })));
            Assert.True(GL.IsEnabled(EnableCap.ClipDistance0));
            Assert.True(GL.IsEnabled(EnableCap.PolygonSmooth)); Assert.False(GL.IsEnabled(EnableCap.ClipDistance1));
            Assert.Equal((int)PointSpriteCoordOriginParameter.LowerLeft, GL.GetInteger((GetPName)All.PointSpriteCoordOrigin));
            GL.Enable((EnableCap)(-1));
            Assert.Throws<InvalidOperationException>(() => cache.SetLineSmooth(false));
            calls = cache.FixedFunctionCalls; cache.SetLineSmooth(false);
            Assert.Equal(calls + 1, cache.FixedFunctionCalls); Assert.False(GL.IsEnabled(EnableCap.LineSmooth));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { cache.ApplyConfigurableRaster(second); }
    }

    /// <summary>Compatibility snapshots preserve disabled parameters and canonical masks across hostile pixel layouts.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompatibilityBoundaryPreservesPatternsAndTransferOwners(bool fail)
    {
        using var context = new CompatibilityContext(fixture, output);
        var cache = StateCache.Current;
        cache.InvalidateAll();
        int pack = GL.GenBuffer(), unpack = GL.GenBuffer();
        var packLayout = new StateCache.PixelPackState(8, 64, 2, 3, true, true);
        var unpackLayout = new StateCache.PixelUnpackState(8, 64, 2, 3, true, true);
        var pattern = new PipelineValues<byte>(Enumerable.Range(0, 128).Select(i => (byte)i));
        try
        {
            cache.BindBuffer(BufferTarget.PixelPackBuffer, pack);
            cache.BindBuffer(BufferTarget.PixelUnpackBuffer, unpack);
            cache.SetPixelPackState(packLayout); cache.SetPixelUnpackState(unpackLayout);
            cache.SetPolygonStipplePattern(pattern);
            cache.SetAlphaFunction(AlphaFunction.Greater, .375f);
            cache.SetLineStipple(7, 0x35aa);
            cache.SetCapability(EnableCap.AlphaTest, false); cache.SetPointSmooth(false);
            cache.SetCapability(EnableCap.LineStipple, false);
            cache.SetCapability(EnableCap.PolygonStipple, false);
            // Cold entry must read dependent parameters even though their enables are disabled.
            cache.Invalidate(EPipelineState.ConfigurableRaster);
            Assert.True(cache.TryBeginEngineBoundary(Declaration(), out var scope));

            var failure = new InvalidOperationException("fixture");
            Action operation = () => { cache.ApplyConfigurableRaster(Pipeline(new() { AlphaTest = true, PointSmooth = true, LineStipple = true, PolygonStipple = true }));
                Assert.True(GL.IsEnabled(EnableCap.AlphaTest)); Assert.True(GL.IsEnabled(EnableCap.PointSmooth));
                Assert.True(GL.IsEnabled(EnableCap.LineStipple)); Assert.True(GL.IsEnabled(EnableCap.PolygonStipple));
                if (fail) throw failure; };
            if (fail) Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => scope!.Run(operation)));
            else scope!.Run(operation);
            Assert.False(GL.IsEnabled(EnableCap.AlphaTest)); Assert.False(GL.IsEnabled(EnableCap.PointSmooth));
            Assert.Equal((int)AlphaFunction.Greater, GL.GetInteger(GetPName.AlphaTestFunc));
            Assert.Equal(.375f, GL.GetFloat(GetPName.AlphaTestRef));
            Assert.Equal(7, GL.GetInteger(GetPName.LineStippleRepeat));
            Assert.Equal(0x35aa, GL.GetInteger(GetPName.LineStipplePattern));
            Assert.Equal(pack, GL.GetInteger(GetPName.PixelPackBufferBinding));
            Assert.Equal(unpack, GL.GetInteger(GetPName.PixelUnpackBufferBinding));
            Assert.Equal(packLayout, cache.GetPixelPackState()); Assert.Equal(unpackLayout, cache.GetPixelUnpackState());
            Assert.Equal(1, GL.GetInteger(GetPName.PackLsbFirst)); Assert.Equal(1, GL.GetInteger(GetPName.UnpackLsbFirst));
            cache.BindBuffer(BufferTarget.PixelPackBuffer, 0); cache.SetPixelPackState(new(1));
            byte[] read = new byte[128]; GL.GetPolygonStipple(read);
            Assert.Equal(pattern.ToArray(), read);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { cache.BindBuffer(BufferTarget.PixelPackBuffer, 0); cache.BindBuffer(BufferTarget.PixelUnpackBuffer, 0); GL.DeleteBuffer(pack); GL.DeleteBuffer(unpack); }
    }

    /// <summary>Real texture uploads invalidate cached pixel layout before a later bitmap transfer borrows it.</summary>
    [Fact]
    public void TextureUploadRefreshesUnpackKnowledge()
    {
        using var context = new CompatibilityContext(fixture, output);
        var cache = StateCache.Current; cache.InvalidateAll();
        using var texture = DynamicTexture2D.Create(1, 1, PixelInternalFormat.R16);
        cache.SetPixelUnpackState(new(8));
        texture.UploadDataImmediate(new ushort[] { 17 });
        Assert.Equal(1, GL.GetInteger(GetPName.UnpackAlignment));
        Assert.Equal(1, cache.GetPixelUnpackState().Alignment);
        cache.SetPolygonStipplePattern(new(new byte[128]));
        Assert.Equal(1, GL.GetInteger(GetPName.UnpackAlignment));
        Assert.Equal(1, cache.GetPixelUnpackState().Alignment);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Transfer callbacks that fail after temporary setup restore native pixel layout and PBO ownership.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransferFailureRestoresTemporaryOwners(bool pack)
    {
        using var context = new CompatibilityContext(fixture, output);
        var cache = StateCache.Current; cache.InvalidateAll();
        int buffer = GL.GenBuffer();
        var target = pack ? BufferTarget.PixelPackBuffer : BufferTarget.PixelUnpackBuffer;
        var packLayout = new StateCache.PixelPackState(8, 64, 2, 3, true, true);
        var unpackLayout = new StateCache.PixelUnpackState(8, 64, 2, 3, true, true);
        try
        {
            cache.BindBuffer(target, buffer);
            cache.SetPixelPackState(packLayout); cache.SetPixelUnpackState(unpackLayout);
            var method = typeof(StateCache).GetMethod("TransferPolygonStipple", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var failure = new InvalidOperationException("transfer fixture");
            Action operation = () => {
                Assert.Equal(0, GL.GetInteger(pack ? GetPName.PixelPackBufferBinding : GetPName.PixelUnpackBufferBinding));
                Assert.Equal(1, GL.GetInteger(pack ? GetPName.PackAlignment : GetPName.UnpackAlignment));
                throw failure;
            };
            var reflectionError = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(cache, [pack, operation]));
            var transferError = Assert.IsType<AggregateException>(reflectionError.InnerException);
            Assert.Contains(failure, transferError.InnerExceptions);
            Assert.Equal(buffer, GL.GetInteger(pack ? GetPName.PixelPackBufferBinding : GetPName.PixelUnpackBufferBinding));
            Assert.Equal(8, GL.GetInteger(pack ? GetPName.PackAlignment : GetPName.UnpackAlignment));
            Assert.Equal(64, GL.GetInteger(pack ? GetPName.PackRowLength : GetPName.UnpackRowLength));
            Assert.Equal(1, GL.GetInteger(pack ? GetPName.PackLsbFirst : GetPName.UnpackLsbFirst));
            Assert.Equal(packLayout, cache.GetPixelPackState()); Assert.Equal(unpackLayout, cache.GetPixelUnpackState());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { cache.BindBuffer(target, 0); GL.DeleteBuffer(buffer); }
    }
    /// <summary>Observed engine bitmap uploads honor their unpack order and withdraw the previous cached payload.</summary>
    [Fact]
    public void EngineBitmapUploadUsesEngineLayout()
    {
        using var context = new CompatibilityContext(fixture, output);
        var cache = StateCache.Current; cache.InvalidateAll();
        var original = new PipelineValues<byte>(Enumerable.Repeat((byte)0xaa, 128));
        cache.SetPolygonStipplePattern(original);
        cache.SetPixelUnpackState(new(1, LsbFirst: true));
        EngineStateCalls.PolygonStipple(Enumerable.Repeat((byte)1, 128).ToArray());
        cache.SetPixelPackState(new(1));
        byte[] actual = new byte[128]; GL.GetPolygonStipple(actual);
        Assert.All(actual, value => Assert.Equal(128, value));
        long calls = cache.FixedFunctionCalls;
        cache.SetPolygonStipplePattern(original);
        Assert.Equal(calls + 1, cache.FixedFunctionCalls);
        GL.GetPolygonStipple(actual); Assert.Equal(original.ToArray(), actual);
        Assert.True(cache.GetPixelUnpackState().LsbFirst);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Real fragments exercise clipping, alpha tests, line/polygon stipple and matching clip-depth conventions.</summary>
    [Fact]
    public void CompatibilityRasterPoliciesChangePixels()
    {
        using var context = new CompatibilityContext(fixture, output);
        using var draw = new RasterDraw();
        var cache = StateCache.Current;
        cache.InvalidateAll();
        Assert.Equal(1024, draw.Count(Pipeline(new())));
        Assert.Equal(512, draw.Count(Pipeline(new() { ClipDistances = 1 })));
        Assert.Equal(0, draw.Count(Pipeline(new() { AlphaTest = true, AlphaComparison = AlphaFunction.Greater, AlphaReference = .75f })));
        Assert.Equal(1024, draw.Count(Pipeline(new() { AlphaTest = true, AlphaComparison = AlphaFunction.Less, AlphaReference = .75f })));
        Assert.Equal(0, draw.Count(Pipeline(new() { PolygonStipple = true, PolygonStipplePattern = new(new byte[128]) })));
        Assert.Equal(512, draw.Count(Pipeline(new() { PolygonStipple = true, PolygonStipplePattern = new(Enumerable.Repeat((byte)0xaa, 128)) })));
        Assert.True(draw.Count(Pipeline(new()), lines: true) > 0);
        Assert.Equal(0, draw.Count(Pipeline(new() { LineStipple = true, LineStipplePattern = 0 }), lines: true));
        if (GpuSupport.Graphics.ClipControl)
        {
            Assert.Equal(0, draw.Count(Pipeline(new() { ClipOrigin = ClipOrigin.UpperLeft, ClipDepth = ClipDepthMode.ZeroToOne }), zeroToOne: false));
            Assert.Equal(1024, draw.Count(Pipeline(new() { ClipOrigin = ClipOrigin.UpperLeft, ClipDepth = ClipDepthMode.ZeroToOne }), zeroToOne: true));
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
    private static GraphicsPipelineDesc Pipeline(RasterizerDesc raster)
    {
        var contract = new GpuShaderContract("raster", [new("raster.vsh", "raster.vsh", ShaderStageKind.Vertex, new()),
            new("raster.fsh", "raster.fsh", ShaderStageKind.Fragment, new())], 1);
        return new(new("test", new(new ShaderSettings(contract))), new([]), new([new(PixelInternalFormat.Rgba8)]), DynamicPipelineState.Viewport,
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
            GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4); GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 3);
            GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Compat);
            GLFW.WindowHint(WindowHintBool.Visible, false);
            window = GLFW.CreateWindow(32, 32, "Raster compatibility test", null, null);
            Assert.SkipWhen(window == null, "Compatibility GL 4.3 context unavailable.");
            GLFW.MakeContextCurrent(window); GL.LoadBindings(new GLFWBindingsContext());
            RenderContextRegistry.RegisterCurrent(this, owner => ((CompatibilityContext)owner).window != null);
            GpuSupport.EnsureCurrentContext(); StateCache.Current.InvalidateAll();
            Assert.False(GpuSupport.Graphics.CoreProfile);
            output.WriteLine($"Raster compatibility: {GL.GetString(StringName.Version)}; {GL.GetString(StringName.Renderer)}; ClipControl={GpuSupport.Graphics.ClipControl}");
        }
        /// <summary>Retires only this fixture lifetime and makes the original fixture current.</summary>
        public void Dispose()
        {
            RenderContextRegistry.Retire(this); GLFW.DestroyWindow(window); window = null;
            fixture.MakeCurrent(); GpuSupport.EnsureCurrentContext(); StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Owns deterministic raw test geometry, shader, target and readback resources.</summary>
    private sealed class RasterDraw : IDisposable
    {
        private readonly int program, vao, framebuffer, color;
        public byte[] LastPixels { get; private set; } = [];
        /// <summary>Creates a 32-square target and vertex-ID geometry with explicitly written clip distances.</summary>
        public RasterDraw()
        {
            int vertex = Compile(ShaderType.VertexShader, "#version 330 core\nout float modelY; uniform int lineMode; uniform int zeroDepth; void main(){vec2 p; if(lineMode!=0) p=vec2(gl_VertexID==0?-1.0:1.0,0.0);else p=vec2((gl_VertexID==1)?3.0:-1.0,(gl_VertexID==2)?3.0:-1.0);gl_Position=vec4(p,zeroDepth!=0?0.25:-0.5,1.0);gl_ClipDistance[0]=p.x;modelY=p.y;}");
            int fragment = Compile(ShaderType.FragmentShader, "#version 330 core\nin float modelY; out vec4 result;void main(){result=vec4(1,modelY>0.0?1.0:0.0,1,0.5);}");
            program = GL.CreateProgram(); GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked); Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.DeleteShader(vertex); GL.DeleteShader(fragment);
            vao = GL.GenVertexArray(); framebuffer = GL.GenFramebuffer(); color = GL.GenRenderbuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, color);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, 32, 32);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, color);
            Assert.Equal(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
            GL.Viewport(0, 0, 32, 32); GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        }
        /// <summary>Applies production raster policy, draws and counts nonzero red pixels.</summary>
        public int Count(GraphicsPipelineDesc pipeline, bool lines = false, bool zeroToOne = false)
        {
            StateCache.Current.ApplyConfigurableRaster(pipeline);
            GL.ClearColor(0, 0, 0, 0); GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.UseProgram(program); GL.BindVertexArray(vao);
            GL.Uniform1(GL.GetUniformLocation(program, "lineMode"), lines ? 1 : 0);
            GL.Uniform1(GL.GetUniformLocation(program, "zeroDepth"), zeroToOne ? 1 : 0);
            GL.DrawArrays(lines ? PrimitiveType.Lines : PrimitiveType.Triangles, 0, lines ? 2 : 3);
            byte[] pixels = new byte[32 * 32 * 4]; GL.ReadPixels(0, 0, 32, 32, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            LastPixels = pixels;
            return Enumerable.Range(0, 1024).Count(i => pixels[i * 4] != 0);
        }
        /// <summary>Deletes native fixture resources before their context is destroyed.</summary>
        public void Dispose() { GL.UseProgram(0); GL.DeleteProgram(program); GL.DeleteVertexArray(vao); GL.DeleteFramebuffer(framebuffer); GL.DeleteRenderbuffer(color); }
        /// <summary>Compiles deterministic fixture source with diagnostic failure output.</summary>
        private static int Compile(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type); GL.ShaderSource(shader, source); GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled); Assert.True(compiled != 0, GL.GetShaderInfoLog(shader)); return shader;
        }
    }
    #endregion
}
