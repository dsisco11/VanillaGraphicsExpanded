using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks descriptor-derived coverage and immutable declaration composition without a native context.</summary>
public sealed class PipelineStateCoverageTests
{
    #region Public API
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
    /// <summary>Unified knowledge masks cannot authorize new fields in partial boundaries.</summary>
    [Fact]
    public void CompleteOnlyFieldsRequireExplicitBoundaryAuthority()
    {
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(depth: DepthStateKnowledge.DepthRange));
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(rasterizer: RasterizerStateKnowledge.DepthClamp));
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(assembly: PrimitiveAssemblyStateKnowledge.RestartIndex));
        Assert.Throws<ArgumentException>(() => new PipelineStateCoverage(dynamic: DynamicDrawStateKnowledge.Scissor));
        var complete = new PipelineStateCoverage(completeGraphics: true);
        Assert.True(complete.Depth.HasFlag(DepthStateKnowledge.DepthRange));
        Assert.True(complete.Rasterizer.HasFlag(RasterizerStateKnowledge.DepthClamp));
        Assert.True(complete.Assembly.HasFlag(PrimitiveAssemblyStateKnowledge.RestartIndex));
        Assert.True(complete.Dynamic.HasFlag(DynamicDrawStateKnowledge.Scissor | DynamicDrawStateKnowledge.BlendConstant));
        Assert.False(complete.Dynamic.HasFlag(DynamicDrawStateKnowledge.Viewport));
    }
    #endregion
}
