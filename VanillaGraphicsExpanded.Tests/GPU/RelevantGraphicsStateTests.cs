using OpenTK.Graphics.OpenGL;
using ErrorCode = OpenTK.Graphics.OpenGL.ErrorCode;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks dormant native state, later activation and union restoration independently of cache contents.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RelevantGraphicsStateTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Wireframe and offsets become active only for their primitive kind.</summary>
    [Fact]
    public void PolygonModesActivateOffsetAndLinePayloads()
    {
        using var context = new WideLineContext(fixture);
        var cache = StateCache.Current; cache.UseProgram(0);
        var fill = Description(raster: new() { LineWidth = 2,
            OffsetLine = true, DepthBiasFactor = 2, DepthBiasUnits = 3 });
        var wire = Description(raster: fill.Rasterizer with { PolygonMode = PolygonMode.Line });
        var point = Description(raster: new() { PolygonMode = PolygonMode.Point, PointSize = 3,
            OffsetPoint = true, DepthBiasFactor = 4, DepthBiasUnits = 5 });
        GL.LineWidth(4);
        GL.PolygonOffset(6, 7); GL.Disable(EnableCap.PolygonOffsetLine); GL.Disable(EnableCap.PolygonOffsetPoint);
        GL.PointSize(5); cache.InvalidateAll();
        Assert.True(CompleteGraphicsBoundary.TryRun("PrimitiveMatrix", [fill, wire, point], [], true, _ =>
        {
            cache.ApplyGraphicsState(fill, Dynamics());
            Assert.Equal(4, GL.GetFloat(GetPName.LineWidth)); Assert.Equal(6, GL.GetFloat(GetPName.PolygonOffsetFactor)); Assert.Equal(7, GL.GetFloat(GetPName.PolygonOffsetUnits));
            cache.ApplyGraphicsState(wire, Dynamics());
            Assert.True(GL.IsEnabled(EnableCap.PolygonOffsetLine)); Assert.Equal(2, GL.GetFloat(GetPName.LineWidth)); Assert.Equal(2, GL.GetFloat(GetPName.PolygonOffsetFactor));
            Assert.Equal(3, GL.GetFloat(GetPName.PolygonOffsetUnits));
            cache.ApplyGraphicsState(point, Dynamics()); Assert.Equal(3, GL.GetFloat(GetPName.PointSize));
            Assert.True(GL.IsEnabled(EnableCap.PolygonOffsetPoint)); Assert.Equal(4, GL.GetFloat(GetPName.PolygonOffsetFactor));
            Assert.Equal(5, GL.GetFloat(GetPName.PolygonOffsetUnits));
            cache.ApplyGraphicsState(fill, Dynamics()); Assert.Equal(3, GL.GetFloat(GetPName.PointSize));
            Assert.Equal(4, GL.GetFloat(GetPName.PolygonOffsetFactor)); Assert.Equal(5, GL.GetFloat(GetPName.PolygonOffsetUnits));
        }));
        Assert.Equal(4, GL.GetFloat(GetPName.LineWidth)); Assert.Equal(5, GL.GetFloat(GetPName.PointSize));
        Assert.False(GL.IsEnabled(EnableCap.PolygonOffsetLine));
        Assert.False(GL.IsEnabled(EnableCap.PolygonOffsetPoint)); Assert.Equal(6, GL.GetFloat(GetPName.PolygonOffsetFactor));
        Assert.Equal(7, GL.GetFloat(GetPName.PolygonOffsetUnits)); Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Unknown executable output cannot inherit dormant line/point values solely from triangle input assembly.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownOutputPrimitiveAppliesConservativeNativeGroups(bool tessellation)
    {
        using var context = new WideLineContext(fixture);
        var cache = StateCache.Current; cache.UseProgram(0);
        var stages = new List<ShaderStageContract> { new("unknown.vsh", "unknown.vsh", ShaderStageKind.Vertex, new()),
            new("unknown.fsh", "unknown.fsh", ShaderStageKind.Fragment, new()) };
        if (tessellation)
        {
            stages.Add(new("unknown.tcs", "unknown.tcs", ShaderStageKind.TessellationControl, new()));
            stages.Add(new("unknown.tes", "unknown.tes", ShaderStageKind.TessellationEvaluation, new()));
        }
        else stages.Add(new("unknown.gsh", "unknown.gsh", ShaderStageKind.Geometry, new()));
        var contract = new GpuShaderContract("unknown-output", stages, 1);
        var description = new GraphicsPipelineDesc(new("fixture", new(new ShaderSettings(contract))), new([]),
            new([new(PixelInternalFormat.Rgba8)]), DynamicPipelineState.Viewport,
            rasterizer: new() { LineWidth = 2, PointSize = 3 },
            assembly: new() { Topology = tessellation ? PrimitiveType.Patches : PrimitiveType.Triangles });
        GL.LineWidth(4); GL.PointSize(5); cache.InvalidateAll();
        Assert.True(CompleteGraphicsBoundary.TryRun("UnknownOutputMatrix", [description], [], true, _ =>
        {
            cache.ApplyGraphicsState(description, Dynamics());
            Assert.Equal(2, GL.GetFloat(GetPName.LineWidth)); Assert.Equal(3, GL.GetFloat(GetPName.PointSize));
        }));
        Assert.Equal(4, GL.GetFloat(GetPName.LineWidth)); Assert.Equal(5, GL.GetFloat(GetPName.PointSize));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Filled triangles retain native width, lines establish intent, and the last triangle cannot narrow handback.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TriangleLineTriangleRestoresOriginalWidth(bool fail)
    {
        using var context = new WideLineContext(fixture);
        var cache = StateCache.Current;
        cache.UseProgram(0);
        var triangle = Description(raster: new() { LineWidth = 2 });
        var line = Description(raster: new() { LineWidth = 2 }, topology: PrimitiveType.Lines);
        GL.LineWidth(4); cache.InvalidateAll(); Assert.Equal(ErrorCode.NoError, GL.GetError());
        var expected = new InvalidOperationException("after final dormant draw");
        try
        {
            bool Run() => CompleteGraphicsBoundary.TryRun("RelevantWidth", [triangle, line], [], true, _ =>
            {
                cache.ApplyGraphicsState(triangle, Dynamics());
                Assert.Equal(4, GL.GetFloat(GetPName.LineWidth));
#if DEBUG
                long calls = cache.FixedFunctionCalls;
#endif
                cache.SetLineWidth(4);
#if DEBUG
                Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
                cache.ApplyGraphicsState(line, Dynamics());
                Assert.Equal(2, GL.GetFloat(GetPName.LineWidth));
                cache.ApplyGraphicsState(triangle, Dynamics());
                Assert.Equal(2, GL.GetFloat(GetPName.LineWidth));
                if (fail) throw expected;
            });
            if (fail) Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => Run()));
            else Assert.True(Run(), cache.BoundaryEntryFailure?.ToString());
            Assert.Equal(4, GL.GetFloat(GetPName.LineWidth));
        }
        finally { GL.LineWidth(1); cache.InvalidateAll(); }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Disabled scissor and shader-sized points retain payloads until explicitly activated, then restore them.</summary>
    [Fact]
    public void ScissorAndPointPayloadsBecomeRelevantWithoutInheritingIntent()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; cache.UseProgram(0);
        var triangle = Description(raster: new() { PointSize = 2 });
        var clipped = Description(raster: new() { Scissor = true });
        var shaderPoints = Description(raster: new() { ProgramPointSize = true, PointSize = 2 }, topology: PrimitiveType.Points);
        var fixedPoints = Description(raster: new() { PointSize = 2 }, topology: PrimitiveType.Points);
        GL.Enable(EnableCap.ScissorTest); GL.Scissor(1, 2, 3, 4); GL.PointSize(4);
        GL.Disable(EnableCap.ProgramPointSize); cache.InvalidateAll();
        try
        {
            Assert.True(CompleteGraphicsBoundary.TryRun("RelevantPayload", [triangle, clipped, shaderPoints, fixedPoints], [], true, _ =>
            {
                cache.ApplyGraphicsState(triangle, Dynamics());
                Assert.False(GL.IsEnabled(EnableCap.ScissorTest)); AssertScissor(1, 2, 3, 4);
                Assert.Equal(4, GL.GetFloat(GetPName.PointSize));
                cache.ApplyGraphicsState(clipped, Dynamics());
                Assert.True(GL.IsEnabled(EnableCap.ScissorTest)); AssertScissor(0, 0, 0, 0);
                cache.ApplyGraphicsState(shaderPoints, Dynamics());
                Assert.True(GL.IsEnabled(EnableCap.ProgramPointSize)); Assert.Equal(4, GL.GetFloat(GetPName.PointSize));
                cache.ApplyGraphicsState(fixedPoints, Dynamics());
                Assert.False(GL.IsEnabled(EnableCap.ProgramPointSize)); Assert.Equal(2, GL.GetFloat(GetPName.PointSize));
                cache.ApplyGraphicsState(triangle, Dynamics());
                Assert.Equal(2, GL.GetFloat(GetPName.PointSize)); AssertScissor(0, 0, 0, 0);
            }));
            Assert.True(GL.IsEnabled(EnableCap.ScissorTest)); AssertScissor(1, 2, 3, 4);
            Assert.False(GL.IsEnabled(EnableCap.ProgramPointSize)); Assert.Equal(4, GL.GetFloat(GetPName.PointSize));
        }
        finally { GL.Disable(EnableCap.ScissorTest); GL.PointSize(1); cache.InvalidateAll(); }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Sparse holes retain indexed payloads; later routing establishes both operands of a combined factor call.</summary>
    [Fact]
    public void SparseOutputActivationPreservesAndRestoresGroupedBlendOperands()
    {
        fixture.MakeCurrent();
        var cache = StateCache.Current;
        cache.UseProgram(0);
        var sparse = Description(targets: new([new(PixelInternalFormat.Rgba8), new(null, true), new(PixelInternalFormat.Rgba8)]));
        var active = Description(targets: sparse.Targets,
            blending: [new(), new(), new() { Enabled = true, RgbEquation = BlendEquationMode.Min,
                AlphaEquation = BlendEquationMode.FuncAdd, SourceRgb = BlendingFactorSrc.SrcAlpha,
                DestinationRgb = BlendingFactorDest.OneMinusSrcAlpha, SourceAlpha = BlendingFactorSrc.ConstantAlpha,
                DestinationAlpha = BlendingFactorDest.OneMinusConstantAlpha }]);
        var routed = Description(targets: new([new(PixelInternalFormat.Rgba8), new(PixelInternalFormat.Rgba8), new(PixelInternalFormat.Rgba8)]),
            blending: [new(), new() { WriteRed = false, WriteBlue = false }, new()]);
        // Slot one is a hole, while slot two transitions from dormant factors to active alpha factors.
        for (int slot = 1; slot <= 2; slot++)
        {
            GL.Enable(IndexedEnableCap.Blend, slot);
            GL.BlendEquationSeparate(slot, BlendEquationMode.Max, BlendEquationMode.FuncSubtract);
            GL.BlendFuncSeparate(slot, BlendingFactorSrc.Zero, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.Zero);
        }
        GL.BlendColor(.2f, .3f, .4f, .5f); cache.InvalidateAll();
        bool[] inheritedMask = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, 1, inheritedMask);
        Assert.True(CompleteGraphicsBoundary.TryRun("RelevantSparseBlend", [sparse, active, routed], [], true, _ =>
        {
            cache.ApplyGraphicsState(sparse, Dynamics());
            Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, 1));
            Assert.False(GL.IsEnabled(IndexedEnableCap.Blend, 2));
            AssertBlendFactors(2, BlendingFactorSrc.Zero, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.Zero);
            cache.ApplyGraphicsState(active, Dynamics());
            AssertBlendFactors(2, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.ConstantAlpha, BlendingFactorDest.OneMinusConstantAlpha);
            cache.ApplyGraphicsState(sparse, Dynamics());
            AssertBlendFactors(2, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.ConstantAlpha, BlendingFactorDest.OneMinusConstantAlpha);
            cache.ApplyGraphicsState(routed, Dynamics());
            Assert.False(GL.IsEnabled(IndexedEnableCap.Blend, 1));
            bool[] mask = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, 1, mask);
            Assert.Equal(new[] { false, true, false, true }, mask);
            cache.ApplyGraphicsState(sparse, Dynamics());
            GL.GetBoolean(GetIndexedPName.ColorWritemask, 1, mask);
            Assert.Equal(new[] { false, true, false, true }, mask);
        }));
        for (int slot = 1; slot <= 2; slot++)
        {
            Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, slot));
            AssertBlendFactors(slot, BlendingFactorSrc.Zero, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.Zero);
            GL.Disable(IndexedEnableCap.Blend, slot);
        }
        float[] color = new float[4]; GL.GetFloat(GetPName.BlendColor, color);
        Assert.Equal(new[] { .2f, .3f, .4f, .5f }, color);
        bool[] restoredMask = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, 1, restoredMask);
        Assert.Equal(inheritedMask, restoredMask);
        cache.InvalidateAll(); Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Provides the only declared dynamic slot, retaining concrete defaults for omitted slots.</summary>
    private static GraphicsDynamicState Dynamics() => new() { Viewport = new() { Width = 8, Height = 8 },
        Scissor = (0, 0, 0, 0), BlendConstant = (0, 0, 0, 0) };

    /// <summary>Builds complete descriptor-only intent without claiming knowledge of an executable's output primitives.</summary>
    private static GraphicsPipelineDesc Description(RasterizerDesc? raster = null, PrimitiveType topology = PrimitiveType.Triangles,
        RenderTargetSignature? targets = null, ColorBlendDesc[]? blending = null)
    {
        var contract = new GpuShaderContract("relevant", [new("relevant.vsh", "relevant.vsh", ShaderStageKind.Vertex, new()),
            new("relevant.fsh", "relevant.fsh", ShaderStageKind.Fragment, new())], 1);
        var dynamics = DynamicPipelineState.Viewport;
        if (raster?.Scissor == true) dynamics |= DynamicPipelineState.Scissor;
        if (blending?.Any(value => value.Enabled && value.SourceAlpha == BlendingFactorSrc.ConstantAlpha) == true)
            dynamics |= DynamicPipelineState.BlendConstant;
        return new(new("fixture", new(new ShaderSettings(contract))), new([]), targets ?? new([new(PixelInternalFormat.Rgba8)]),
            dynamics, rasterizer: raster, blending: blending, assembly: new() { Topology = topology });
    }

    /// <summary>Reads the actual inherited rectangle rather than the declaration or cache.</summary>
    private static void AssertScissor(int x, int y, int width, int height)
    {
        int[] rectangle = new int[4]; GL.GetInteger(GetPName.ScissorBox, rectangle);
        Assert.Equal(new[] { x, y, width, height }, rectangle);
    }

    /// <summary>Checks every operand of the shared RGB/alpha native factor setter.</summary>
    private static void AssertBlendFactors(int slot, BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha)
    {
        var parameters = new[] { GetPName.BlendSrcRgb, GetPName.BlendDstRgb, GetPName.BlendSrcAlpha, GetPName.BlendDstAlpha };
        int[] expected = [(int)sourceRgb, (int)destinationRgb, (int)sourceAlpha, (int)destinationAlpha];
        for (int index = 0; index < parameters.Length; index++)
        {
            GL.GetInteger((GetIndexedPName)parameters[index], slot, out int value); Assert.Equal(expected[index], value);
        }
    }

    /// <summary>Provides non-forward-compatible raster support for the required nondefault native line widths.</summary>
    private sealed unsafe class WideLineContext : IDisposable
    {
        private readonly HeadlessGLFixture fixture;
        private Window* window;

        #region Public API
        /// <summary>Registers an isolated compatibility context without changing the collection context's capabilities.</summary>
        public WideLineContext(HeadlessGLFixture fixture)
        {
            this.fixture = fixture; fixture.MakeCurrent();
            // Forward-compatible core contexts reject non-unit widths even when the driver reports a wider range.
            GLFW.DefaultWindowHints(); GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4);
            GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 5); GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Compat);
            GLFW.WindowHint(WindowHintBool.Visible, false); GLFW.WindowHint(WindowHintBool.OpenGLForwardCompat, false);
            window = GLFW.CreateWindow(8, 8, "Relevant width test", null, null);
            Assert.True(window != null, "The required compatibility raster context could not be created.");
            GLFW.MakeContextCurrent(window); GL.LoadBindings(new GLFWBindingsContext());
            RenderContextRegistry.RegisterCurrent(this, owner => ((WideLineContext)owner).window != null);
            GpuSupport.EnsureCurrentContext(); StateCache.Current.InvalidateAll();
        }

        /// <summary>Retires this context before restoring the shared fixture and its state owner.</summary>
        public void Dispose()
        {
            RenderContextRegistry.Retire(this); GLFW.DestroyWindow(window); window = null;
            fixture.MakeCurrent(); GpuSupport.EnsureCurrentContext(); StateCache.Current.InvalidateAll();
        }
        #endregion
    }
    #endregion
}
