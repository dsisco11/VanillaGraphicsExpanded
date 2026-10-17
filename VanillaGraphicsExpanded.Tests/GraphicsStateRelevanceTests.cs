using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks immutable potential native write groups independently of active state application.</summary>
public sealed class GraphicsStateRelevanceTests
{
    #region Public API
    #region Primitive and raster dependencies
    /// <summary>Filled polygons retain facing policy while sizes and disabled payloads remain dormant.</summary>
    [Fact]
    public void FilledTrianglesKeepFacingButExcludeLineAndPointPayloads()
    {
        var value = Derive();
        Assert.True(value.Rasterizer.HasFlag(RasterizerStateKnowledge.CullEnabled | RasterizerStateKnowledge.FrontFace
            | RasterizerStateKnowledge.PolygonModes | RasterizerStateKnowledge.PolygonSmooth));
        Assert.False(value.Rasterizer.HasFlag(RasterizerStateKnowledge.CullMode));
        Assert.Equal(RasterizerStateKnowledge.None, value.Rasterizer & (RasterizerStateKnowledge.LineWidth
            | RasterizerStateKnowledge.LineSmooth | RasterizerStateKnowledge.LineStipple | RasterizerStateKnowledge.PointSize
            | RasterizerStateKnowledge.ProgramPointSize | RasterizerStateKnowledge.PointSpriteOrigin));
        Assert.True(value.Rasterizer.HasFlag(RasterizerStateKnowledge.ScissorEnabled));
        Assert.False(value.Dynamic.HasFlag(DynamicDrawStateKnowledge.Scissor));
        Assert.False(value.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonOffset));
        Assert.True(Derive(raster: new() { Cull = true }).Rasterizer.HasFlag(RasterizerStateKnowledge.CullMode));
        Assert.True(Derive(raster: new() { Scissor = true }).Dynamic.HasFlag(DynamicDrawStateKnowledge.Scissor));
    }

    /// <summary>Input lines and wireframe polygons both require line controls, while only polygons use facing.</summary>
    [Theory]
    [InlineData(PrimitiveType.Lines)]
    [InlineData(PrimitiveType.LineStrip)]
    [InlineData(PrimitiveType.LineLoop)]
    [InlineData(PrimitiveType.LinesAdjacency)]
    [InlineData(PrimitiveType.LineStripAdjacency)]
    public void LinePrimitivesUseLineGroupsWithoutPolygonFacing(PrimitiveType topology)
    {
        var value = Derive(assembly: new() { Topology = topology });
        Assert.True(value.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.LineSmooth
            | RasterizerStateKnowledge.LineStipple));
        Assert.False(value.Rasterizer.HasFlag(RasterizerStateKnowledge.FrontFace));
        Assert.False(value.Rasterizer.HasFlag(RasterizerStateKnowledge.LineStippleParameters));
        var wireframe = Derive(raster: new() { PolygonMode = PolygonMode.Line, LineStipple = true });
        Assert.True(wireframe.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.LineSmooth
            | RasterizerStateKnowledge.LineStipple | RasterizerStateKnowledge.LineStippleParameters
            | RasterizerStateKnowledge.FrontFace | RasterizerStateKnowledge.PolygonModes));
    }

    /// <summary>Programmable point size suppresses only its fixed-size operand, retaining point policy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointSizeModeControlsOnlyFixedSize(bool programmable)
    {
        var value = Derive(assembly: new() { Topology = PrimitiveType.Points }, raster: new() { ProgramPointSize = programmable });
        Assert.Equal(!programmable, value.Rasterizer.HasFlag(RasterizerStateKnowledge.PointSize));
        Assert.True(value.Rasterizer.HasFlag(RasterizerStateKnowledge.ProgramPointSize
            | RasterizerStateKnowledge.PointSpriteOrigin | RasterizerStateKnowledge.PointSmooth));
        Assert.False(value.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonModes));
        Assert.False(value.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth));
        Assert.True(Derive(raster: new() { PolygonMode = PolygonMode.Point }).Rasterizer.HasFlag(RasterizerStateKnowledge.PointSize));
    }

    /// <summary>Polygon offset payloads require the enable corresponding to the selected polygon mode.</summary>
    [Theory]
    [InlineData(PolygonMode.Fill)]
    [InlineData(PolygonMode.Line)]
    [InlineData(PolygonMode.Point)]
    public void PolygonOffsetsFollowSelectedRasterMode(PolygonMode mode)
    {
        var all = Derive(raster: new() { PolygonMode = mode, OffsetFill = true, OffsetLine = true, OffsetPoint = true });
        Assert.True(all.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonOffset));
        if (mode != PolygonMode.Fill)
            Assert.Equal(RasterizerStateKnowledge.None, all.Rasterizer & (RasterizerStateKnowledge.PolygonSmooth
                | RasterizerStateKnowledge.PolygonStipple | RasterizerStateKnowledge.PolygonStipplePattern));
        var inactive = Derive(raster: new() { PolygonMode = mode, OffsetFill = mode != PolygonMode.Fill,
            OffsetLine = mode != PolygonMode.Line, OffsetPoint = mode != PolygonMode.Point });
        Assert.False(inactive.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonOffset));
        Assert.True(Derive(raster: new() { PolygonStipple = true }).Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonStipplePattern));
        Assert.False(Derive().Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonStipplePattern));
    }

    /// <summary>Geometry and unprepared tessellation cannot infer emitted raster primitives from input assembly.</summary>
    [Fact]
    public void UnknownShaderOutputsKeepAllPrimitiveGroupsConservative()
    {
        foreach (var value in new[] { Derive(extraStages: [ShaderStageKind.Geometry], assembly: new() { Topology = PrimitiveType.Points }),
            Derive(extraStages: [ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation],
                assembly: new() { Topology = PrimitiveType.Patches }) })
        {
            Assert.True(value.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.PointSize
                | RasterizerStateKnowledge.PolygonModes | RasterizerStateKnowledge.FrontFace));
        }
    }
    #endregion

    #region Aspect, output and grouped dependencies
    /// <summary>Attachment aspects determine test-enable relevance and enabled tests retain all paired operands.</summary>
    [Fact]
    public void DepthStencilAspectsAndEnablesControlPayloads()
    {
        var absent = Derive();
        Assert.Equal(DepthStateKnowledge.DepthRange, absent.Depth);
        Assert.Equal(StencilStateKnowledge.None, absent.Stencil);
        var target = new RenderTargetSignature([new(PixelInternalFormat.Rgba16f)], PixelInternalFormat.Depth24Stencil8);
        var disabled = Derive(targets: target);
        Assert.Equal(DepthStateKnowledge.DepthRange | DepthStateKnowledge.TestEnabled, disabled.Depth);
        Assert.Equal(StencilStateKnowledge.TestEnabled, disabled.Stencil);
        var enabled = Derive(targets: target, depth: new() { DepthTest = true, StencilTest = true });
        Assert.Equal(DepthStateKnowledge.All, enabled.Depth);
        Assert.Equal(StencilStateKnowledge.All, enabled.Stencil);
    }

    /// <summary>Output locations, sparse holes and integer formats determine write masks without slot neutralization.</summary>
    [Fact]
    public void SparseAndIntegerOutputsKeepOnlyApplicableNativeGroups()
    {
        var target = new RenderTargetSignature([new(null, true), new(PixelInternalFormat.Rgba16f), new(PixelInternalFormat.Rgba8ui)]);
        var value = Derive(targets: target, blends: [new(), new() { Enabled = true }, new()]);
        Assert.Equal(3, value.Blending.Count);
        Assert.Equal(BlendStateKnowledge.None, value.Blending[0]);
        Assert.Equal(BlendStateKnowledge.All, value.Blending[1]);
        Assert.Equal(BlendStateKnowledge.WriteMask, value.Blending[2]);
        Assert.Equal(BlendStateKnowledge.None, value.GlobalBlend);
    }

    /// <summary>MIN/MAX ignore factors and constants, but either active channel retains its complete combined native group.</summary>
    [Fact]
    public void EquationsAndConstantFactorsRespectCombinedWrites()
    {
        var min = new ColorBlendDesc { Enabled = true, RgbEquation = BlendEquationMode.Min,
            AlphaEquation = BlendEquationMode.Max, SourceRgb = BlendingFactorSrc.ConstantColor };
        var unused = Derive(blends: [min]);
        Assert.Equal(BlendStateKnowledge.Enabled | BlendStateKnowledge.Equations | BlendStateKnowledge.WriteMask, unused.Blending[0]);
        Assert.False(unused.Dynamic.HasFlag(DynamicDrawStateKnowledge.BlendConstant));
        var factor = Derive(blends: [min with { AlphaEquation = BlendEquationMode.FuncAdd }]);
        Assert.Equal(BlendStateKnowledge.All, factor.Blending[0]);
        Assert.False(factor.Dynamic.HasFlag(DynamicDrawStateKnowledge.BlendConstant));
        var constant = Derive(blends: [min with { AlphaEquation = BlendEquationMode.FuncAdd, SourceAlpha = BlendingFactorSrc.ConstantAlpha }]);
        Assert.True(constant.Dynamic.HasFlag(DynamicDrawStateKnowledge.BlendConstant));
        Assert.False(Derive(blends: [min with { Enabled = false }]).Dynamic.HasFlag(DynamicDrawStateKnowledge.BlendConstant));
    }

    /// <summary>Global factor/equation setters retain their all-output alias while enables/masks remain indexed.</summary>
    [Fact]
    public void NonIndependentBlendUsesGlobalNativeGroups()
    {
        var value = Derive(caps: Capabilities() with { IndependentBlend = false }, blends: [new() { Enabled = true }]);
        Assert.Equal(BlendStateKnowledge.Equations | BlendStateKnowledge.Factors, value.GlobalBlend);
        Assert.Equal(BlendStateKnowledge.Enabled | BlendStateKnowledge.WriteMask, value.Blending[0]);
        var min = new ColorBlendDesc { Enabled = true, RgbEquation = BlendEquationMode.Min, AlphaEquation = BlendEquationMode.Max };
        var target = new RenderTargetSignature([new(PixelInternalFormat.Rgba16f), new(PixelInternalFormat.Rgba16f)]);
        // Ignored authored factors may differ without requiring independent native equations/factors.
        var ignored = Derive(caps: Capabilities() with { IndependentBlend = false }, targets: target,
            blends: [min, min with { SourceRgb = BlendingFactorSrc.SrcAlpha }]);
        Assert.Equal(BlendStateKnowledge.Equations, ignored.GlobalBlend);
    }
    #endregion

    #region Sampling, assembly and conservative rules
    /// <summary>Single-sample targets retain sampling enables; only enabled payloads participate.</summary>
    [Fact]
    public void SamplingPayloadsFollowFeatureEnables()
    {
        var value = Derive();
        var enables = SamplingStateKnowledge.Multisample | SamplingStateKnowledge.SampleCoverageEnabled
            | SamplingStateKnowledge.SampleMask | SamplingStateKnowledge.SampleAlphaToCoverage
            | SamplingStateKnowledge.SampleAlphaToOne | SamplingStateKnowledge.SampleShading;
        Assert.Equal(enables, value.Sampling);
        Assert.False(value.SampleMaskWords);
        var enabled = Derive(sampling: new() { CoverageEnabled = true, MaskEnabled = true, SampleShading = true });
        Assert.Equal(SamplingStateKnowledge.All, enabled.Sampling);
        Assert.True(enabled.SampleMaskWords);
    }

    /// <summary>Restart enables remain conservative while explicit-index and patch payloads follow selectors.</summary>
    [Fact]
    public void AssemblyPayloadsFollowRestartAndPatchSelectors()
    {
        var enables = PrimitiveAssemblyStateKnowledge.PrimitiveRestart | PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex;
        Assert.Equal(enables, Derive().Assembly);
        Assert.Equal(enables | PrimitiveAssemblyStateKnowledge.RestartIndex, Derive(assembly: new() { Restart = true }).Assembly);
        Assert.Equal(enables, Derive(assembly: new() { FixedIndexRestart = true }).Assembly);
        Assert.Equal(enables | PrimitiveAssemblyStateKnowledge.PatchVertices,
            Derive(extraStages: [ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation],
                assembly: new() { Topology = PrimitiveType.Patches }).Assembly);
    }

    /// <summary>Unproved state dependencies remain conservative and deriving metadata cannot narrow active authority.</summary>
    [Fact]
    public void ConservativeGroupsAndCurrentCoverageRemainComplete()
    {
        var description = Description();
        var value = GraphicsStateRelevance.Derive(description, Capabilities());
        Assert.True(value.Rasterizer.HasFlag(RasterizerStateKnowledge.ProvokingVertex | RasterizerStateKnowledge.ClipDistances
            | RasterizerStateKnowledge.ClipControl | RasterizerStateKnowledge.DepthClamp | RasterizerStateKnowledge.RasterizerDiscard));
        Assert.Equal(OutputStateKnowledge.All, value.Output);
        Assert.True(value.Dynamic.HasFlag(DynamicDrawStateKnowledge.Viewport));
        var coverage = PipelineStateCoverage.From(description);
        Assert.True(coverage.CompleteGraphics);
        Assert.True(coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.PointSize));
        Assert.True(coverage.Dynamic.HasFlag(DynamicDrawStateKnowledge.Scissor | DynamicDrawStateKnowledge.BlendConstant));
    }

    /// <summary>Prepared relevance converts into sparse complete authority without widening dormant native groups.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparedRelevanceRetainsSparseGroupsAndGlobalAliases(bool independent)
    {
        var target = new RenderTargetSignature([new(null, true), new(PixelInternalFormat.Rgba16f), new(PixelInternalFormat.Rgba8ui)]);
        var relevance = Derive(caps: Capabilities() with { IndependentBlend = independent }, targets: target,
            blends: [new(), new() { Enabled = true }, new()], sampling: new() { MaskEnabled = true });
        var coverage = PipelineStateCoverage.From(relevance);
        Assert.True(coverage.CompleteGraphics);
        Assert.Equal(relevance.Depth, coverage.Depth); Assert.Equal(relevance.Stencil, coverage.Stencil);
        Assert.Equal(relevance.Rasterizer, coverage.Rasterizer); Assert.Equal(relevance.Dynamic, coverage.Dynamic);
        Assert.Equal(relevance.Assembly, coverage.Assembly); Assert.Equal(relevance.Sampling, coverage.Sampling);
        Assert.Equal(relevance.Output, coverage.Output); Assert.Equal(relevance.SampleMaskWords, coverage.SampleMaskWords);
        Assert.Equal(relevance.GlobalBlend, coverage.GlobalBlend);
        for (int slot = 0; slot < 8; slot++)
            Assert.Equal(relevance.GlobalBlend | (slot < relevance.Blending.Count ? relevance.Blending[slot] : BlendStateKnowledge.None),
                coverage.BlendAt(slot));
        Assert.False(coverage.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.PointSize));
        Assert.False(coverage.Dynamic.HasFlag(DynamicDrawStateKnowledge.Scissor));
        Assert.False(coverage.Contains(new PipelineStateCoverage(rasterizer: RasterizerStateKnowledge.LineWidth), 8));
        Assert.False(coverage.ClearColor);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Derives metadata with controlled capabilities and without native shader queries.</summary>
    private static GraphicsStateRelevance Derive(GraphicsCapabilities? caps = null, RenderTargetSignature? targets = null,
        DepthStencilDesc? depth = null, RasterizerDesc? raster = null, ColorBlendDesc[]? blends = null,
        SamplingDesc? sampling = null, PrimitiveAssemblyDesc? assembly = null, ShaderStageKind[]? extraStages = null) =>
        GraphicsStateRelevance.Derive(Description(caps, targets, depth, raster, blends, sampling, assembly, extraStages), caps ?? Capabilities());

    /// <summary>Builds one complete structural declaration with all dynamic slots explicitly required.</summary>
    private static GraphicsPipelineDesc Description(GraphicsCapabilities? caps = null, RenderTargetSignature? targets = null,
        DepthStencilDesc? depth = null, RasterizerDesc? raster = null, ColorBlendDesc[]? blends = null,
        SamplingDesc? sampling = null, PrimitiveAssemblyDesc? assembly = null, ShaderStageKind[]? extraStages = null)
    {
        var kinds = new[] { ShaderStageKind.Vertex, ShaderStageKind.Fragment }.Concat(extraStages ?? []);
        var contract = new GpuShaderContract("relevance", kinds.Select(kind =>
            new ShaderStageContract(kind.ToString(), kind.ToString(), kind, new())), 1);
        return new(new("test", new(new ShaderSettings(contract))), new([]), targets ?? new([new(PixelInternalFormat.Rgba16f)]),
            DynamicPipelineState.All, caps ?? Capabilities(), depth, raster, blends, sampling, assembly);
    }

    /// <summary>Supplies deterministic supported limits and a compatibility profile without acquiring a native context.</summary>
    private static GraphicsCapabilities Capabilities() => new()
    {
        Graphics33 = true, IndependentBlend = true, SampleShading = true, Tessellation = true,
        FixedIndexRestart = true, DepthClamp = true, ClipControl = true, MaxClipDistances = 8,
        MaxDrawBuffers = 8, MaxVertexAttributes = 16, MaxVertexBindings = 16,
        MaxVertexStride = 2048, MaxVertexRelativeOffset = 2047, MaxSamples = 8, MaxSampleMaskWords = 2,
        MaxPatchVertices = 32, MinLineWidth = 1, MaxLineWidth = 16, MinPointSize = 1, MaxPointSize = 64
    };
    #endregion
}
