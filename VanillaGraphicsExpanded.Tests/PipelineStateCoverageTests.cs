using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks descriptor-derived coverage and immutable declaration composition without a native context.</summary>
public sealed class PipelineStateCoverageTests
{
    #region Public API
    #region Partial descriptor contracts
    /// <summary>Both default and explicit intents map to exactly the same affected native fields.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DescriptorIntentsMapToAllSupportedFields(bool explicitValues)
    {
        var mask = new GlPipelineStateMask(GlPipelineStateMask.ValidBits);
        var descriptor = new GlPipelineDesc(explicitValues ? default : mask, explicitValues ? mask : default,
            depthFunc: OpenTK.Graphics.OpenGL.DepthFunction.Greater, depthWriteMask: false,
            blendFunc: GlBlendFunc.Default, blendEnableIndexedAttachments: [2],
            blendFuncIndexed: [new GlBlendFuncIndexed(3, GlBlendFunc.Default)],
            colorMask: GlColorMask.All, lineWidth: 2, pointSize: 3);
        var coverage = PipelineStateCoverage.From(descriptor);
        Assert.Equal((DepthStateKnowledge.TestEnabled | DepthStateKnowledge.Comparison | DepthStateKnowledge.WriteEnabled), coverage.Depth);
        Assert.Equal(RasterizerStateKnowledge.CullEnabled | RasterizerStateKnowledge.ScissorEnabled | RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.PointSize, coverage.Rasterizer);
        Assert.Equal(PrimitiveAssemblyStateKnowledge.None, coverage.Assembly);
        Assert.Equal(DynamicDrawStateKnowledge.None, coverage.Dynamic);
        Assert.False(coverage.ClearColor);
        for (int i = 0; i < 8; i++) Assert.Equal(BlendStateKnowledge.Enabled | BlendStateKnowledge.Factors | BlendStateKnowledge.WriteMask, coverage.BlendAt(i));
    }

    /// <summary>Declarations union dynamic/helper coverage and copy indexed intents before exposed source arrays change.</summary>
    [Fact]
    public void DeclarationDoesNotAliasSourcePayloads()
    {
        var descriptor = new GlPipelineDesc(default,
            GlPipelineStateMask.From(GlPipelineStateId.BlendEnableIndexed) | GlPipelineStateMask.From(GlPipelineStateId.BlendFuncIndexed),
            blendEnableIndexedAttachments: [1], blendFuncIndexed: [new GlBlendFuncIndexed(1, GlBlendFunc.Default)]);
        var helper = new Dictionary<int, BlendStateKnowledge> { [2] = BlendStateKnowledge.WriteMask };
        var declaration = new EngineBoundaryDeclaration("Immutable", PipelineStateCoverage.From(descriptor),
            new PipelineStateCoverage(dynamic: DynamicDrawStateKnowledge.Viewport, clearColor: true, indexedBlend: helper));
        descriptor.BlendEnableIndexedAttachments![0] = 5;
        descriptor.BlendFuncIndexed![0] = new GlBlendFuncIndexed(5, GlBlendFunc.Default);
        helper[2] = BlendStateKnowledge.All;
        Assert.Equal(BlendStateKnowledge.Enabled | BlendStateKnowledge.Factors, declaration.Coverage.BlendAt(1));
        Assert.Equal(BlendStateKnowledge.WriteMask, declaration.Coverage.BlendAt(2));
        Assert.Equal(BlendStateKnowledge.None, declaration.Coverage.BlendAt(5));
        Assert.Equal(DynamicDrawStateKnowledge.Viewport, declaration.Coverage.Dynamic);
        Assert.True(declaration.Coverage.ClearColor);
    }

    /// <summary>Alias containment is based on every supported output rather than a global marker alone.</summary>
    [Fact]
    public void CompleteIndexedCoverageCanAuthorizeGlobalOperation()
    {
        var coverage = new PipelineStateCoverage(indexedBlend: new Dictionary<int, BlendStateKnowledge>
        {
            [0] = BlendStateKnowledge.Enabled, [1] = BlendStateKnowledge.Enabled
        });
        var global = new PipelineStateCoverage(globalBlend: BlendStateKnowledge.Enabled);
        Assert.True(coverage.Contains(global, 2));
        Assert.False(coverage.Contains(global, 3));
        Assert.False(coverage.Contains(new PipelineStateCoverage(globalBlend: BlendStateKnowledge.Factors), 2));
    }

    /// <summary>Unknown or malformed descriptor intent cannot silently become an empty authorization.</summary>
    [Fact]
    public void UnsupportedCoverageAndMalformedDescriptorsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipelineStateCoverage(depth: (DepthStateKnowledge)128));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipelineStateCoverage(indexedBlend:
            new Dictionary<int, BlendStateKnowledge> { [-1] = BlendStateKnowledge.Enabled }));
        var descriptor = new GlPipelineDesc(new GlPipelineStateMask(1UL << 63), default, validate: false);
        Assert.Throws<ArgumentException>(() => PipelineStateCoverage.From(descriptor));
        var missing = new GlPipelineDesc(default, GlPipelineStateMask.From(GlPipelineStateId.BlendEnableIndexed), validate: false);
        Assert.Throws<ArgumentException>(() => PipelineStateCoverage.From(missing));
    }
    #endregion
    #region Sparse complete authority
    /// <summary>Complete draw admission does not implicitly authorize dormant or unmentioned native writes.</summary>
    [Fact]
    public void CompleteOnlyFieldsRequireExplicitBoundaryAuthority()
    {
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(depth: DepthStateKnowledge.DepthRange));
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(rasterizer: RasterizerStateKnowledge.DepthClamp));
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(assembly: PrimitiveAssemblyStateKnowledge.RestartIndex));
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(dynamic: DynamicDrawStateKnowledge.Scissor));
        var complete = new PipelineStateCoverage(completeGraphics: true);
        Assert.True(complete.CompleteGraphics);
        Assert.Equal(DepthStateKnowledge.None, complete.Depth);
        Assert.Equal(RasterizerStateKnowledge.None, complete.Rasterizer);
        Assert.Equal(PrimitiveAssemblyStateKnowledge.None, complete.Assembly);
        Assert.Equal(DynamicDrawStateKnowledge.None, complete.Dynamic);
        Assert.Equal(StencilStateKnowledge.None, complete.Stencil);
        Assert.Equal(SamplingStateKnowledge.None, complete.Sampling);
        Assert.Equal(OutputStateKnowledge.None, complete.Output);
        Assert.False(complete.SampleMaskWords);
        Assert.False(complete.Contains(new PipelineStateCoverage(rasterizer: RasterizerStateKnowledge.LineWidth), 8));
    }

    /// <summary>Sparse native groups compose independently of complete admission and keep caller payloads detached.</summary>
    [Fact]
    public void SparseCompleteGroupsUnionWithoutExpandingTheirAuthority()
    {
        var first = new PipelineStateCoverage(depth: DepthStateKnowledge.DepthRange, completeGraphics: true,
            stencil: StencilStateKnowledge.FrontStencilFunction, sampling: SamplingStateKnowledge.SampleCoverage);
        var second = new PipelineStateCoverage(dynamic: DynamicDrawStateKnowledge.BlendConstant, completeGraphics: true,
            output: OutputStateKnowledge.Dither, sampleMaskWords: true,
            indexedBlend: new Dictionary<int, BlendStateKnowledge> { [2] = BlendStateKnowledge.Factors });
        var combined = first.Union(second);
        Assert.True(combined.Contains(first, 8)); Assert.True(combined.Contains(second, 8));
        Assert.False(first.Contains(second, 8)); Assert.False(second.Contains(first, 8));
        Assert.Equal(DepthStateKnowledge.DepthRange, combined.Depth);
        Assert.Equal(StencilStateKnowledge.FrontStencilFunction, combined.Stencil);
        Assert.Equal(SamplingStateKnowledge.SampleCoverage, combined.Sampling);
        Assert.Equal(OutputStateKnowledge.Dither, combined.Output);
        Assert.True(combined.SampleMaskWords);
        Assert.Equal(RasterizerStateKnowledge.None, combined.Rasterizer);
        Assert.Equal(BlendStateKnowledge.None, combined.BlendAt(1));
        Assert.Equal(BlendStateKnowledge.Factors, combined.BlendAt(2));
        Assert.False(combined.Contains(new PipelineStateCoverage(completeGraphics: true,
            stencil: StencilStateKnowledge.BackStencilFunction), 8));
        Assert.False(combined.Contains(new PipelineStateCoverage(completeGraphics: true,
            sampling: SamplingStateKnowledge.SampleMask), 8));
        Assert.False(combined.Contains(new PipelineStateCoverage(completeGraphics: true,
            output: OutputStateKnowledge.LogicOperation), 8));
        Assert.False(new PipelineStateCoverage(completeGraphics: true).Contains(
            new PipelineStateCoverage(completeGraphics: true, sampleMaskWords: true), 8));
        Assert.False(PipelineStateCoverage.Empty.Contains(new PipelineStateCoverage(completeGraphics: true), 8));
    }

    /// <summary>Sparse blend slots cannot narrow the native effects of global combined factor and equation setters.</summary>
    [Fact]
    public void SparseBlendUnionPreservesGlobalNativeAliases()
    {
        var local = new PipelineStateCoverage(indexedBlend: new Dictionary<int, BlendStateKnowledge>
        { [1] = BlendStateKnowledge.Factors | BlendStateKnowledge.Equations });
        var aliases = new PipelineStateCoverage(globalBlend: BlendStateKnowledge.Factors | BlendStateKnowledge.Equations);
        Assert.False(local.Contains(aliases, 4));
        var combined = local.Union(aliases);
        for (int slot = 0; slot < 4; slot++)
            Assert.Equal(BlendStateKnowledge.Factors | BlendStateKnowledge.Equations, combined.BlendAt(slot));
        Assert.True(combined.Contains(aliases, 4));
        Assert.False(combined.Contains(new PipelineStateCoverage(globalBlend: BlendStateKnowledge.WriteMask), 4));
        Assert.Equal(BlendStateKnowledge.None, local.BlendAt(0));
    }

    /// <summary>New complete category masks reject malformed bits and cannot widen legacy partial helper contracts.</summary>
    [Fact]
    public void NewSparseGroupsRequireValidMasksAndCompleteAdmission()
    {
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(stencil: StencilStateKnowledge.TestEnabled));
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(sampling: SamplingStateKnowledge.SampleCoverage));
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(output: OutputStateKnowledge.Dither));
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(sampleMaskWords: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipelineStateCoverage(completeGraphics: true,
            stencil: (StencilStateKnowledge)128));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipelineStateCoverage(completeGraphics: true,
            output: (OutputStateKnowledge)128));
    }

    /// <summary>Clear helper authority covers only its bounded actual native writes, including all-output color-mask aliases.</summary>
    [Fact]
    public void ClearEnvelopeIsIndependentFromCompleteDrawFields()
    {
        var value = OpenGLRenderPassOperations.ClearCoverage;
        Assert.True(value.CompleteGraphics);
        Assert.Equal(DepthStateKnowledge.WriteEnabled, value.Depth);
        Assert.Equal(RasterizerStateKnowledge.RasterizerDiscard | RasterizerStateKnowledge.ScissorEnabled, value.Rasterizer);
        Assert.Equal(DynamicDrawStateKnowledge.Scissor, value.Dynamic);
        Assert.Equal(StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask, value.Stencil);
        Assert.Equal(OutputStateKnowledge.FramebufferSrgb | OutputStateKnowledge.Dither, value.Output);
        Assert.Equal(SamplingStateKnowledge.None, value.Sampling);
        Assert.Equal(PrimitiveAssemblyStateKnowledge.None, value.Assembly);
        Assert.False(value.SampleMaskWords); Assert.False(value.ClearColor);
        for (int slot = 0; slot < 8; slot++) Assert.Equal(BlendStateKnowledge.WriteMask, value.BlendAt(slot));
        Assert.False(value.Contains(PipelineStateCoverage.FullGraphics, 8));
        Assert.True(PipelineStateCoverage.FullGraphics.Contains(value, 8));
    }
    #endregion
    #endregion
}
