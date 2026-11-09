using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Pipelines;

/// <summary>Checks potential native write groups from shader generations compiled through the headless GL fixture.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GraphicsStateRelevanceRulesTests : IDisposable
{
    private readonly GraphicsStateShaders stateShaders = new();
    #region Public API
    /// <summary>Retires test-owned executable generations after state assertions complete.</summary>
    public void Dispose() => stateShaders.Dispose();

    /// <summary>Acquires the fixture context before creating ready shader references.</summary>
    public GraphicsStateRelevanceRulesTests(HeadlessGLFixture fixture) { fixture.MakeCurrent(); }

    #region Primitive and raster dependencies
    /// <summary>Filled polygons retain facing policy while sizes and disabled payloads remain dormant.</summary>
    [Fact]
    public void FilledTrianglesKeepFacingButExcludeLineAndPointPayloads()
    {
        // Arrange and act
        var value = Derive();
        // Assert
        Assert.True(value.Rasterizer.HasFlag(RasterizerStateKnowledge.CullEnabled | RasterizerStateKnowledge.FrontFace
            | RasterizerStateKnowledge.PolygonModes | RasterizerStateKnowledge.PolygonSmooth));
        Assert.False(value.Rasterizer.HasFlag(RasterizerStateKnowledge.CullMode));
        Assert.Equal(RasterizerStateKnowledge.None, value.Rasterizer & (RasterizerStateKnowledge.LineWidth
            | RasterizerStateKnowledge.LineSmooth | RasterizerStateKnowledge.PointSize
            | RasterizerStateKnowledge.ProgramPointSize | RasterizerStateKnowledge.PointSpriteOrigin));
        Assert.True(value.Rasterizer.HasFlag(RasterizerStateKnowledge.ScissorEnabled));
        Assert.False(value.Dynamic.HasFlag(DynamicDrawStateKnowledge.Scissor));
        Assert.False(value.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonOffset));
        // Act and assert enabled culling and scissor settings retain their additional groups.
        Assert.True(Derive(raster: new() { Cull = true }).Rasterizer.HasFlag(RasterizerStateKnowledge.CullMode));
        Assert.True(Derive(raster: new() { Scissor = true }).Dynamic.HasFlag(DynamicDrawStateKnowledge.Scissor));
    }

    /// <summary>Input lines and wireframe polygons both require line controls, while only polygons use facing.</summary>
    [Theory]
    [InlineData(PrimitiveTopology.Lines)]
    [InlineData(PrimitiveTopology.LineStrip)]
    [InlineData(PrimitiveTopology.LineLoop)]
    [InlineData(PrimitiveTopology.LinesAdjacency)]
    [InlineData(PrimitiveTopology.LineStripAdjacency)]
    public void LinePrimitivesUseLineGroupsWithoutPolygonFacing(PrimitiveTopology topology)
    {
        // Arrange and act
        var value = Derive(assembly: new() { Topology = topology });
        // Assert
        Assert.True(value.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.LineSmooth));
        Assert.False(value.Rasterizer.HasFlag(RasterizerStateKnowledge.FrontFace));
        // Act and assert wireframe polygons additionally require polygon-facing state.
        var wireframe = Derive(raster: new() { PolygonMode = RasterizerFillMode.Line });
        Assert.True(wireframe.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.LineSmooth
            | RasterizerStateKnowledge.FrontFace | RasterizerStateKnowledge.PolygonModes));
    }

    /// <summary>Programmable point size suppresses only its fixed-size operand, retaining point policy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointSizeModeControlsOnlyFixedSize(bool programmable)
    {
        // Arrange and act
        var value = Derive(assembly: new() { Topology = PrimitiveTopology.Points }, raster: new() { ProgramPointSize = programmable });
        // Assert
        Assert.Equal(!programmable, value.Rasterizer.HasFlag(RasterizerStateKnowledge.PointSize));
        Assert.True(value.Rasterizer.HasFlag(RasterizerStateKnowledge.ProgramPointSize
            | RasterizerStateKnowledge.PointSpriteOrigin));
        Assert.False(value.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonModes));
        Assert.False(value.Rasterizer.HasFlag(RasterizerStateKnowledge.LineWidth));
        // Act and assert fixed point-size state remains relevant for point polygon mode.
        Assert.True(Derive(raster: new() { PolygonMode = RasterizerFillMode.Point }).Rasterizer.HasFlag(RasterizerStateKnowledge.PointSize));
    }

    /// <summary>Polygon offset payloads require the enable corresponding to the selected polygon mode.</summary>
    [Theory]
    [InlineData(RasterizerFillMode.Fill)]
    [InlineData(RasterizerFillMode.Line)]
    [InlineData(RasterizerFillMode.Point)]
    public void PolygonOffsetsFollowSelectedRasterMode(RasterizerFillMode mode)
    {
        // Arrange and act
        var all = Derive(raster: new() { PolygonMode = mode, OffsetFill = true, OffsetLine = true, OffsetPoint = true });
        // Assert
        Assert.True(all.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonOffset));
        if (mode != RasterizerFillMode.Fill)
            Assert.Equal(RasterizerStateKnowledge.None, all.Rasterizer & (RasterizerStateKnowledge.PolygonSmooth));
        // Act and assert inactive mode-specific enables do not admit polygon-offset state.
        var inactive = Derive(raster: new() { PolygonMode = mode, OffsetFill = mode != RasterizerFillMode.Fill,
            OffsetLine = mode != RasterizerFillMode.Line, OffsetPoint = mode != RasterizerFillMode.Point });
        Assert.False(inactive.Rasterizer.HasFlag(RasterizerStateKnowledge.PolygonOffset));
    }

    /// <summary>Geometry and unprepared tessellation cannot infer emitted raster primitives from input assembly.</summary>
    [Fact]
    public void UnknownShaderOutputsKeepAllPrimitiveGroupsConservative()
    {
        // Arrange and act
        foreach (var value in new[] { Derive(extraStages: [ShaderStageKind.Geometry], assembly: new() { Topology = PrimitiveTopology.Points }),
            Derive(extraStages: [ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation],
                assembly: new() { Topology = PrimitiveTopology.Patches }) })
        {
            // Assert unknown shader output topology keeps each possible raster group active.
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
        // Arrange and act
        var absent = Derive();
        // Assert
        Assert.Equal(DepthStateKnowledge.DepthRange, absent.Depth);
        Assert.Equal(StencilStateKnowledge.None, absent.Stencil);
        // Act and assert attachment aspects add the appropriate depth and stencil enables.
        var target = new RenderTargetSignature([new(TextureFormat.Rgba16f)], TextureFormat.Depth24Stencil8);
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
        // Arrange and act
        var target = new RenderTargetSignature([new(null, true), new(TextureFormat.Rgba16f), new(TextureFormat.Rgba8ui)]);
        var value = Derive(targets: target, blends: [new(), new() { Enabled = true }, new()]);
        // Assert
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
        // Arrange
        var min = new ColorBlendDesc { Enabled = true, RgbEquation = BlendOperation.Minimum,
            AlphaEquation = BlendOperation.Maximum, SourceRgb = BlendFactor.ConstantColor };
        // Act
        var unused = Derive(blends: [min]);
        // Assert
        Assert.Equal(BlendStateKnowledge.Enabled | BlendStateKnowledge.Equations | BlendStateKnowledge.WriteMask, unused.Blending[0]);
        Assert.False(unused.Dynamic.HasFlag(DynamicDrawStateKnowledge.BlendConstant));
        // Act and assert a non-MIN/MAX channel makes factors relevant without enabling a constant.
        var factor = Derive(blends: [min with { AlphaEquation = BlendOperation.Add }]);
        Assert.Equal(BlendStateKnowledge.All, factor.Blending[0]);
        Assert.False(factor.Dynamic.HasFlag(DynamicDrawStateKnowledge.BlendConstant));
        var constant = Derive(blends: [min with { AlphaEquation = BlendOperation.Add, SourceAlpha = BlendFactor.ConstantAlpha }]);
        Assert.True(constant.Dynamic.HasFlag(DynamicDrawStateKnowledge.BlendConstant));
        Assert.False(Derive(blends: [min with { Enabled = false }]).Dynamic.HasFlag(DynamicDrawStateKnowledge.BlendConstant));
    }

    /// <summary>Global factor/equation setters retain their all-output alias while enables/masks remain indexed.</summary>
    [Fact]
    public void NonIndependentBlendUsesGlobalNativeGroups()
    {
        // Arrange and act
        var value = Derive(caps: Capabilities() with { IndependentBlend = false }, blends: [new() { Enabled = true }]);
        // Assert
        Assert.Equal(BlendStateKnowledge.Equations | BlendStateKnowledge.Factors, value.GlobalBlend);
        Assert.Equal(BlendStateKnowledge.Enabled | BlendStateKnowledge.WriteMask, value.Blending[0]);
        // Act and assert global equation/factor aliases across multiple outputs.
        var min = new ColorBlendDesc { Enabled = true, RgbEquation = BlendOperation.Minimum, AlphaEquation = BlendOperation.Maximum };
        var target = new RenderTargetSignature([new(TextureFormat.Rgba16f), new(TextureFormat.Rgba16f)]);
        // Ignored authored factors may differ without requiring independent native equations/factors.
        var ignored = Derive(caps: Capabilities() with { IndependentBlend = false }, targets: target,
            blends: [min, min with { SourceRgb = BlendFactor.SourceAlpha }]);
        Assert.Equal(BlendStateKnowledge.Equations, ignored.GlobalBlend);
    }
    #endregion

    #region Sampling, assembly and conservative rules
    /// <summary>Single-sample targets retain sampling enables; only enabled payloads participate.</summary>
    [Fact]
    public void SamplingPayloadsFollowFeatureEnables()
    {
        // Arrange and act
        var value = Derive();
        // Assert
        var enables = SamplingStateKnowledge.Multisample | SamplingStateKnowledge.SampleCoverageEnabled
            | SamplingStateKnowledge.SampleMask | SamplingStateKnowledge.SampleAlphaToCoverage
            | SamplingStateKnowledge.SampleAlphaToOne | SamplingStateKnowledge.SampleShading;
        Assert.Equal(enables, value.Sampling);
        Assert.False(value.SampleMaskWords);
        // Act and assert enabling the optional sampling payloads admits their full state groups.
        var enabled = Derive(sampling: new() { CoverageEnabled = true, MaskEnabled = true, SampleShading = true });
        Assert.Equal(SamplingStateKnowledge.All, enabled.Sampling);
        Assert.True(enabled.SampleMaskWords);
    }

    /// <summary>Restart enables remain conservative while explicit-index and patch payloads follow selectors.</summary>
    [Fact]
    public void AssemblyPayloadsFollowRestartAndPatchSelectors()
    {
        // Arrange and act
        var enables = PrimitiveAssemblyStateKnowledge.PrimitiveRestart | PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex;
        // Act and assert restart selectors and tessellation stages reveal only their paired payloads.
        Assert.Equal(enables, Derive().Assembly);
        Assert.Equal(enables | PrimitiveAssemblyStateKnowledge.RestartIndex, Derive(assembly: new() { Restart = true }).Assembly);
        Assert.Equal(enables, Derive(assembly: new() { FixedIndexRestart = true }).Assembly);
        Assert.Equal(enables | PrimitiveAssemblyStateKnowledge.PatchVertices,
            Derive(extraStages: [ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation],
                assembly: new() { Topology = PrimitiveTopology.Patches }).Assembly);
    }

    /// <summary>Unproved state dependencies remain conservative and deriving metadata cannot narrow active authority.</summary>
    [Fact]
    public void ConservativeGroupsAndCurrentCoverageRemainComplete()
    {
        // Arrange
        var description = Description();
        // Act
        var value = GraphicsStateRelevance.Derive(description, Capabilities());
        // Assert
        Assert.True(value.Rasterizer.HasFlag(RasterizerStateKnowledge.ProvokingVertex | RasterizerStateKnowledge.ClipDistances
            | RasterizerStateKnowledge.ClipControl | RasterizerStateKnowledge.DepthClamp | RasterizerStateKnowledge.RasterizerDiscard));
        Assert.Equal(OutputStateKnowledge.All, value.Output);
        Assert.True(value.Dynamic.HasFlag(DynamicDrawStateKnowledge.Viewport));
        // Act and assert current derived relevance admits the complete corresponding coverage groups.
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
        // Arrange and act
        var target = new RenderTargetSignature([new(null, true), new(TextureFormat.Rgba16f), new(TextureFormat.Rgba8ui)]);
        var relevance = Derive(caps: Capabilities() with { IndependentBlend = independent }, targets: target,
            blends: [new(), new() { Enabled = true }, new()], sampling: new() { MaskEnabled = true });
        var coverage = PipelineStateCoverage.From(relevance);
        // Assert
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
    /// <summary>Derives metadata from a fixture-compiled shader description and controlled capabilities.</summary>
    private GraphicsStateRelevance Derive(GraphicsCapabilities? caps = null, RenderTargetSignature? targets = null,
        DepthStencilDesc? depth = null, RasterizerDesc? raster = null, ColorBlendDesc[]? blends = null,
        SamplingDesc? sampling = null, PrimitiveAssemblyDesc? assembly = null, ShaderStageKind[]? extraStages = null) =>
        GraphicsStateRelevance.Derive(Description(caps, targets, depth, raster, blends, sampling, assembly, extraStages), caps ?? Capabilities());

    /// <summary>Builds one complete structural declaration with all dynamic slots explicitly required.</summary>
    private RHIGraphicsPipelineDescription Description(GraphicsCapabilities? caps = null, RenderTargetSignature? targets = null,
        DepthStencilDesc? depth = null, RasterizerDesc? raster = null, ColorBlendDesc[]? blends = null,
        SamplingDesc? sampling = null, PrimitiveAssemblyDesc? assembly = null, ShaderStageKind[]? extraStages = null)
    {
        var kinds = new[] { ShaderStageKind.Vertex, ShaderStageKind.Fragment }.Concat(extraStages ?? []);
        var contract = new GpuShaderContract("relevance", kinds.Select(kind =>
            new ShaderStageContract(kind.ToString(), kind.ToString(), kind, new())), 1);
        return new(stateShaders.Get(contract.Stages.Select(stage => stage.Kind)), new([]), targets ?? new([new(TextureFormat.Rgba16f)]),
            DynamicPipelineState.All, depth, raster, blends, sampling, assembly);
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
