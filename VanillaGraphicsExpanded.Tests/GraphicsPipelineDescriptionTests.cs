using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Exercises immutable graphics description contracts without native graphics state.</summary>
public sealed class GraphicsPipelineDescriptionTests
{
    #region Public API
    #region Construction and publication
    /// <summary>Omissions resolve to project defaults and independent inputs produce equal reusable identities.</summary>
    [Fact]
    public void DefaultsAndCopiedDescriptionsHaveStructuralIdentity()
    {
        var first = Create(label: "first");
        var second = Create(label: "second");
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.False(first == null);
        Assert.True(first != null);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first.DepthStencil.DepthTest);
        Assert.False(first.DepthStencil.DepthWrite);
        Assert.Equal(DepthFunction.Less, first.DepthStencil.DepthComparison);
        Assert.Equal(0, first.DepthStencil.DepthRangeNear);
        Assert.Equal(1, first.DepthStencil.DepthRangeFar);
        Assert.False(first.Rasterizer.Cull);
        Assert.Equal(PolygonMode.Fill, first.Rasterizer.PolygonMode);
        Assert.Equal(1, first.Rasterizer.LineWidth);
        Assert.Equal(1, first.Rasterizer.PointSize);
        Assert.Equal(PrimitiveType.Triangles, first.Assembly.Topology);
        Assert.True(first.Sampling.Multisample);
        Assert.Equal(uint.MaxValue, first.Sampling.GetMaskWord(0));
        Assert.False(first.Output.FramebufferSrgb);
        Assert.False(first.Output.Dither);
        Assert.False(first.Output.LogicOperationEnabled);
        Assert.All(first.Blending, value => Assert.Equal(new ColorBlendDesc(), value));
    }

    /// <summary>Caller-owned collections cannot mutate target, vertex, blend or sample-mask identity after publication.</summary>
    [Fact]
    public void PublishedCollectionsDoNotAliasCallerStorage()
    {
        ColorTargetSlot[] colors = [new(PixelInternalFormat.Rgba16f)];
        VertexAttributeDesc[] attributes = [Attribute()];
        ColorBlendDesc[] blends = [new()];
        uint[] masks = [0x12345678, uint.MaxValue];
        var pipeline = Create(targets: new(colors), layout: new(attributes), blending: blends,
            sampling: new() { MaskEnabled = true, Masks = new(masks) });
        int hash = pipeline.GetHashCode();
        colors[0] = new(PixelInternalFormat.Rgba32f);
        attributes[0] = Attribute() with { Location = 2 };
        blends[0] = new() { WriteRed = false };
        masks[0] = 0;
        Assert.Equal(PixelInternalFormat.Rgba16f, pipeline.Targets.Colors[0].Format);
        Assert.Equal(0, pipeline.VertexLayout.Attributes[0].Location);
        Assert.True(pipeline.Blending[0].WriteRed);
        Assert.Equal(0x12345678u, pipeline.Sampling.GetMaskWord(0));
        Assert.Equal(hash, pipeline.GetHashCode());
        Assert.False((object)pipeline.Blending is IList<ColorBlendDesc>);
    }

    /// <summary>Inactive native values canonicalize while later enabled descriptions retain their complete intent.</summary>
    [Fact]
    public void DisabledValuesCanonicalizeWithoutLosingEnabledIntent()
    {
        Assert.Equal(Create(), Create(rasterizer: new() { CullMode = CullFaceMode.Front, DepthBiasFactor = 3 },
            depth: new() { Front = new() { Pass = StencilOp.Replace } },
            blending: [new() { SourceRgb = BlendingFactorSrc.SrcAlpha }],
            sampling: new() { Coverage = .3f, CoverageInvert = true, MinimumSampleShading = .5f },
            assembly: new() { RestartIndex = 17, PatchVertices = 7 }));
        Assert.NotEqual(Create(rasterizer: new() { Cull = true }),
            Create(rasterizer: new() { Cull = true, CullMode = CullFaceMode.Front }));
        Assert.NotEqual(Create(blending: [new() { Enabled = true }]),
            Create(blending: [new() { Enabled = true, SourceRgb = BlendingFactorSrc.SrcAlpha }]));
        Assert.NotEqual(Create(), Create(blending: [new() { WriteAlpha = false }]));
    }

    /// <summary>All draw-relevant description families participate in reusable identity.</summary>
    [Fact]
    public void ActiveConfigurationChangesIdentity()
    {
        var baseline = Create();
        GraphicsPipelineDesc[] variants = [
            Create(shader: Shader("other")),
            Create(layout: new([Attribute() with { Divisor = 1 }])),
            Create(targets: new([new(PixelInternalFormat.Rgba32f)])),
            Create(dynamics: DynamicPipelineState.Viewport | DynamicPipelineState.Scissor),
            Create(rasterizer: new() { FrontFace = FrontFaceDirection.Cw }),
            Create(rasterizer: new() { ProgramPointSize = true }),
            Create(rasterizer: new() { OffsetFill = true, DepthBiasUnits = 1 }),
            Create(rasterizer: new() { Discard = true }),
            Create(depth: new() { DepthTest = true }, targets: new([new(PixelInternalFormat.Rgba16f)], PixelInternalFormat.DepthComponent24)),
            Create(sampling: new() { AlphaToCoverage = true }),
            Create(sampling: new() { Multisample = false }),
            Create(assembly: new() { Topology = PrimitiveType.Lines }),
            Create(output: new() { Dither = true })
        ];
        foreach (var variant in variants) Assert.NotEqual(baseline, variant);
    }

    #endregion
    #region Validation and target contracts
    /// <summary>Viewport and feature-dependent dynamic values must be explicitly declared.</summary>
    [Fact]
    public void MissingDynamicDeclarationsRejectInEveryConfiguration()
    {
        Assert.ThrowsAny<ArgumentException>(() => Create(dynamics: DynamicPipelineState.None));
        Assert.ThrowsAny<ArgumentException>(() => Create(dynamics: (DynamicPipelineState)128));
        Assert.ThrowsAny<ArgumentException>(() => Create(rasterizer: new() { Scissor = true }));
        Assert.ThrowsAny<ArgumentException>(() => Create(depth: new() { StencilTest = true },
            targets: new([new(PixelInternalFormat.Rgba16f)], PixelInternalFormat.Depth24Stencil8)));
        Assert.ThrowsAny<ArgumentException>(() => Create(blending: [new() { Enabled = true, SourceRgb = BlendingFactorSrc.ConstantColor }]));
        Create(rasterizer: new() { Scissor = true }, dynamics: DynamicPipelineState.Viewport | DynamicPipelineState.Scissor);
        Create(depth: new() { StencilTest = true }, targets: new([new(PixelInternalFormat.Rgba16f)], PixelInternalFormat.Depth24Stencil8),
            dynamics: DynamicPipelineState.Viewport | DynamicPipelineState.StencilReference);
        Create(blending: [new() { Enabled = true, SourceRgb = BlendingFactorSrc.ConstantColor }],
            dynamics: DynamicPipelineState.Viewport | DynamicPipelineState.BlendConstant);
    }

    /// <summary>Formats remain exact, sparse holes retain their output location, and storage sample zero normalizes to one.</summary>
    [Fact]
    public void TargetSignaturesPreserveExactFormatsAndSparseDiscardPolicy()
    {
        Assert.Equal(new RenderTargetSignature([new(PixelInternalFormat.Rgba8)], samples: 0),
            new RenderTargetSignature([new(PixelInternalFormat.Rgba8)], samples: 1));
        Assert.NotEqual(new RenderTargetSignature([new(PixelInternalFormat.Rgba8)]), new RenderTargetSignature([new(PixelInternalFormat.Srgb8Alpha8)]));
        var sparse = new RenderTargetSignature([new(null, true), new(PixelInternalFormat.Rgba16f)]);
        Assert.Equal(2, sparse.Colors.Count);
        Assert.Null(sparse.Colors[0].Format);
        Assert.NotEqual(sparse, new RenderTargetSignature([new(null), new(PixelInternalFormat.Rgba16f)]));
        Assert.NotEqual(sparse, new RenderTargetSignature([new(PixelInternalFormat.Rgba16f), new(null, true)]));
        Assert.ThrowsAny<ArgumentException>(() => new RenderTargetSignature([new(PixelInternalFormat.Rgba)]));
        Assert.ThrowsAny<ArgumentException>(() => new RenderTargetSignature([new(PixelInternalFormat.Rgba8, true)]));
        Assert.ThrowsAny<ArgumentException>(() => new RenderTargetSignature([], samples: -1));
    }

    /// <summary>Device limits reject otherwise valid descriptions without leaking capability identity into reusable keys.</summary>
    [Fact]
    public void CapabilityLimitsBoundDescriptionsWithoutChangingIdentity()
    {
        Assert.Equal(Create(), Create(capabilities: Capabilities() with { MaxSamples = 16, MaxDrawBuffers = 16 }));
        Assert.Throws<NotSupportedException>(() => Create(targets: new([new(PixelInternalFormat.Rgba16f)], samples: 16)));
        Assert.Throws<NotSupportedException>(() => Create(layout: new([Attribute() with { Location = 16 }])));
        Assert.Throws<NotSupportedException>(() => Create(layout: new([Attribute() with { Binding = 16 }])));
        Assert.Throws<NotSupportedException>(() => Create(layout: new([Attribute() with { Stride = 4096 }])));
        Assert.ThrowsAny<ArgumentException>(() => Create(rasterizer: new() { LineWidth = 17 }));
        Assert.ThrowsAny<ArgumentException>(() => Create(rasterizer: new() { PointSize = 65 }));
        Assert.ThrowsAny<ArgumentException>(() => Create(depth: new() { DepthComparison = (DepthFunction)(-1) }));
        Assert.ThrowsAny<ArgumentException>(() => Create(depth: new() { StencilTest = true, Front = new() { ReadMask = 256 } },
            targets: new([new(PixelInternalFormat.Rgba16f)], PixelInternalFormat.Depth24Stencil8), dynamics: DynamicPipelineState.Viewport | DynamicPipelineState.StencilReference));
    }
    /// <summary>Malformed layouts and unsupported scalar representations reject before pipeline publication.</summary>
    [Fact]
    public void VertexLayoutsValidatePackingAndCanonicalizeOrder()
    {
        var first = Attribute();
        var second = Attribute() with { Location = 1, Binding = 1 };
        Assert.Equal(new VertexLayoutDesc([first, second]), new VertexLayoutDesc([second, first]));
        Assert.ThrowsAny<ArgumentException>(() => new VertexLayoutDesc([first, first]));
        Assert.ThrowsAny<ArgumentException>(() => new VertexLayoutDesc([first with { Components = 5 }]));
        Assert.ThrowsAny<ArgumentException>(() => new VertexLayoutDesc([first with { Offset = 1 }]));
        Assert.ThrowsAny<ArgumentException>(() => new VertexLayoutDesc([first with { Interpretation = VertexInterpretation.Integer }]));
        Assert.ThrowsAny<ArgumentException>(() => new VertexLayoutDesc([first, first with { Location = 1, Divisor = 1 }]));
    }

    /// <summary>Invalid combinations, nonfinite values and unavailable native features reject in Release as well as Debug.</summary>
    [Fact]
    public void InvalidCombinationsAndCapabilitiesReject()
    {
        Assert.ThrowsAny<ArgumentException>(() => Create(depth: new() { DepthTest = true }));
        Assert.ThrowsAny<ArgumentException>(() => Create(blending: []));
        Assert.ThrowsAny<ArgumentException>(() => Create(blending: [new() { Enabled = true }], targets: new([new(PixelInternalFormat.Rgba8ui)])));
        Assert.ThrowsAny<ArgumentException>(() => Create(rasterizer: new() { LineWidth = float.NaN }));
        Assert.ThrowsAny<ArgumentException>(() => Create(rasterizer: new() { PointSize = 0 }));
        Assert.ThrowsAny<ArgumentException>(() => Create(sampling: new() { CoverageEnabled = true, Coverage = 2 }));
        Assert.Throws<NotSupportedException>(() => Create(sampling: new() { MaskEnabled = true, Masks = new([1u, 2u, 3u]) }));
        Assert.ThrowsAny<ArgumentException>(() => Create(assembly: new() { Topology = PrimitiveType.Patches }));
        Assert.Throws<NotSupportedException>(() => Create(capabilities: Capabilities() with { Graphics33 = false }));
        Assert.ThrowsAny<ArgumentException>(() => Create(capabilities: Capabilities() with { MaxDrawBuffers = 0 }));
        Assert.Throws<NotSupportedException>(() => Create(capabilities: Capabilities() with { DepthClamp = false }, rasterizer: new() { DepthClamp = true }));
        Assert.Throws<NotSupportedException>(() => Create(capabilities: Capabilities() with { SampleShading = false }, sampling: new() { SampleShading = true }));
        Assert.Throws<NotSupportedException>(() => Create(capabilities: Capabilities() with { FixedIndexRestart = false }, assembly: new() { FixedIndexRestart = true }));
    }
    #endregion
    #region Field-sensitive identity
    /// <summary>Every mutable raster option changes identity when its controlling feature participates.</summary>
    [Fact]
    public void RasterFieldsAreIndependentlyIdentified()
    {
        var baseline = new RasterizerDesc { Cull = true, OffsetFill = true };
        RasterizerDesc[] variants = [baseline with { Cull = false }, baseline with { CullMode = CullFaceMode.Front },
            baseline with { FrontFace = FrontFaceDirection.Cw }, baseline with { PolygonMode = PolygonMode.Line },
            baseline with { OffsetFill = false }, baseline with { OffsetLine = true }, baseline with { OffsetPoint = true },
            baseline with { DepthBiasFactor = 1 }, baseline with { DepthBiasUnits = 1 }, baseline with { DepthClamp = true },
            baseline with { Discard = true }, baseline with { Scissor = true },
            baseline with { ProvokingVertex = ProvokingVertexMode.FirstVertexConvention }, baseline with { LineWidth = 2 },
            baseline with { PointSize = 2 }, baseline with { ProgramPointSize = true }];
        var original = Create(rasterizer: baseline, dynamics: DynamicPipelineState.Viewport | DynamicPipelineState.Scissor);
        foreach (var changed in variants)
            Assert.NotEqual(original, Create(rasterizer: changed, dynamics: DynamicPipelineState.Viewport | DynamicPipelineState.Scissor));
    }

    /// <summary>Front and back stencil operations and masks are separate from one another and depth policy.</summary>
    [Fact]
    public void DepthAndStencilFieldsAreIndependentlyIdentified()
    {
        var target = new RenderTargetSignature([new(PixelInternalFormat.Rgba16f)], PixelInternalFormat.Depth24Stencil8);
        var baseline = new DepthStencilDesc { DepthTest = true, StencilTest = true };
        var original = Create(depth: baseline, targets: target, dynamics: DynamicPipelineState.Viewport | DynamicPipelineState.StencilReference);
        StencilFaceDesc[] faces = [new() { Comparison = StencilFunction.Equal }, new() { ReadMask = 15 },
            new() { WriteMask = 15 }, new() { Fail = StencilOp.Replace }, new() { DepthFail = StencilOp.Incr }, new() { Pass = StencilOp.Invert }];
        foreach (var face in faces)
        {
            Assert.NotEqual(original, Create(depth: baseline with { Front = face }, targets: target, dynamics: original.Dynamics));
            Assert.NotEqual(original, Create(depth: baseline with { Back = face }, targets: target, dynamics: original.Dynamics));
        }
        foreach (var changed in new[] { baseline with { DepthTest = false }, baseline with { DepthComparison = DepthFunction.Greater },
            baseline with { DepthWrite = true }, baseline with { StencilTest = false } })
            Assert.NotEqual(original, Create(depth: changed, targets: target, dynamics: original.Dynamics));
    }

    /// <summary>Independent equations, factors and write channels all belong to output identity.</summary>
    [Fact]
    public void BlendFieldsAreIndependentlyIdentified()
    {
        var baseline = new ColorBlendDesc { Enabled = true };
        ColorBlendDesc[] variants = [baseline with { Enabled = false }, baseline with { RgbEquation = BlendEquationMode.FuncSubtract },
            baseline with { AlphaEquation = BlendEquationMode.FuncReverseSubtract }, baseline with { SourceRgb = BlendingFactorSrc.SrcAlpha },
            baseline with { DestinationRgb = BlendingFactorDest.One }, baseline with { SourceAlpha = BlendingFactorSrc.Zero },
            baseline with { DestinationAlpha = BlendingFactorDest.One }, baseline with { WriteRed = false },
            baseline with { WriteGreen = false }, baseline with { WriteBlue = false }, baseline with { WriteAlpha = false }];
        var original = Create(blending: [baseline]);
        foreach (var changed in variants) Assert.NotEqual(original, Create(blending: [changed]));
    }

    /// <summary>Sampling state includes every mask word and all enabled controls.</summary>
    [Fact]
    public void SamplingFieldsAreIndependentlyIdentified()
    {
        var baseline = new SamplingDesc { CoverageEnabled = true, MaskEnabled = true, Masks = new([1u, 2u]), SampleShading = true };
        SamplingDesc[] variants = [baseline with { Multisample = false }, baseline with { CoverageEnabled = false },
            baseline with { Coverage = .5f }, baseline with { CoverageInvert = true }, baseline with { MaskEnabled = false },
            baseline with { Masks = new([2u, 2u]) }, baseline with { Masks = new([1u, 3u]) }, baseline with { AlphaToCoverage = true },
            baseline with { AlphaToOne = true }, baseline with { SampleShading = false }, baseline with { MinimumSampleShading = .5f }];
        Assert.Equal(Create(sampling: new() { MaskEnabled = true, Masks = new([1u]) }), Create(sampling: new() { MaskEnabled = true, Masks = new([1u, uint.MaxValue]) }));
        Assert.Equal(uint.MaxValue, Create(sampling: new() { MaskEnabled = true, Masks = new([1u]) }).Sampling.GetMaskWord(1));
        Assert.Equal(Create(sampling: new() { MaskEnabled = true }), Create(sampling: new() { MaskEnabled = true, Masks = new([uint.MaxValue, uint.MaxValue]) }));
        var original = Create(sampling: baseline);
        foreach (var changed in variants) Assert.NotEqual(original, Create(sampling: changed));
    }

    /// <summary>Shader variant specialization and resource-layout changes cannot alias a prior reusable key.</summary>
    [Fact]
    public void ShaderSpecializationsAndBindingLayoutsChangeIdentity()
    {
        var option = new ShaderOption<int>("QUALITY", 1, [1, 2]);
        /// <summary>Rebuilds each owner so equality cannot succeed through shared references.</summary>
        ShaderPipelineIdentity Identity(int quality, int slot)
        {
            var selected = new GpuBindingContract();
            selected.RegisterUniformBlockBinding("Parameters", slot);
            var contract = new GpuShaderContract("test", [
                new ShaderStageContract("test.vsh", "test.vsh", ShaderStageKind.Vertex, selected),
                new ShaderStageContract("test.fsh", "test.fsh", ShaderStageKind.Fragment, selected,
                    specializations: [new(0, option)])], 1, options: [option]);
            return new("test", new(new ShaderSettings(contract).With(option, quality)));
        }
        Assert.Equal(Identity(1, 0), Identity(1, 0));
        Assert.NotEqual(Identity(1, 0), Identity(2, 0));
        Assert.NotEqual(Identity(1, 0), Identity(1, 1));
    }

    /// <summary>Primitive restart and paired tessellation configuration remain explicit and capability bounded.</summary>
    [Fact]
    public void AssemblyAndTessellationAreValidatedAndIdentified()
    {
        Assert.NotEqual(Create(), Create(assembly: new() { Restart = true }));
        Assert.NotEqual(Create(assembly: new() { Restart = true }), Create(assembly: new() { Restart = true, RestartIndex = 12 }));
        Assert.NotEqual(Create(), Create(assembly: new() { FixedIndexRestart = true }));
        Assert.ThrowsAny<ArgumentException>(() => Create(assembly: new() { Restart = true, FixedIndexRestart = true }));
        var stages = new[] { ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation };
        var contract = new GpuShaderContract("tess", stages.Select(kind => new ShaderStageContract(kind.ToString(), kind.ToString(), kind, new())), 1);
        var shader = new ShaderPipelineIdentity("test", new(new ShaderSettings(contract)));
        var assembly = new PrimitiveAssemblyDesc { Topology = PrimitiveType.Patches };
        Assert.NotEqual(Create(shader: shader, assembly: assembly), Create(shader: shader, assembly: assembly with { PatchVertices = 4 }));
        Assert.Throws<NotSupportedException>(() => Create(shader: shader, assembly: assembly, capabilities: Capabilities() with { Tessellation = false }));
        Assert.Throws<NotSupportedException>(() => Create(shader: shader, assembly: assembly with { PatchVertices = 33 }));
        Assert.ThrowsAny<ArgumentException>(() => Create(shader: shader));
    }
    #endregion
    #region Compatibility
    /// <summary>Legacy partial masks retain their stable wire numbering and cannot be used as complete descriptions.</summary>
    [Fact]
    public void LegacyOverridesRemainASeparateStableContract()
    {
        GlPipelineStateId[] ids = [GlPipelineStateId.DepthTestEnable, GlPipelineStateId.DepthFunc, GlPipelineStateId.DepthWriteMask,
            GlPipelineStateId.BlendEnable, GlPipelineStateId.BlendFunc, GlPipelineStateId.BlendEnableIndexed, GlPipelineStateId.BlendFuncIndexed,
            GlPipelineStateId.CullFaceEnable, GlPipelineStateId.ColorMask, GlPipelineStateId.ScissorTestEnable, GlPipelineStateId.LineWidth, GlPipelineStateId.PointSize];
        for (int index = 0; index < ids.Length; index++) Assert.Equal(index, (int)ids[index]);
        Assert.Equal(12, (int)GlPipelineStateId.Count);
        Assert.False(typeof(GraphicsPipelineDesc).IsAssignableFrom(typeof(GlPipelineDesc)));
        Assert.DoesNotContain(typeof(GraphicsPipelineDesc).GetConstructors(), constructor =>
            constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(GlPipelineDesc)));
    }
    #endregion

    #endregion

    #region Private
    /// <summary>Builds a representative fullscreen description while allowing one contract to vary.</summary>
    private static GraphicsPipelineDesc Create(ShaderPipelineIdentity? shader = null, VertexLayoutDesc? layout = null,
        RenderTargetSignature? targets = null, DynamicPipelineState dynamics = DynamicPipelineState.Viewport,
        GraphicsCapabilities? capabilities = null, DepthStencilDesc? depth = null, RasterizerDesc? rasterizer = null,
        IEnumerable<ColorBlendDesc>? blending = null, SamplingDesc? sampling = null, PrimitiveAssemblyDesc? assembly = null,
        OutputDesc? output = null, string? label = null) => new(shader ?? Shader(), layout ?? new([Attribute()]),
            targets ?? new([new(PixelInternalFormat.Rgba16f)]), dynamics, capabilities ?? Capabilities(),
            depth, rasterizer, blending, sampling, assembly, output, label);

    /// <summary>Projects independent shader declarations through the production load-plan contract.</summary>
    private static ShaderPipelineIdentity Shader(string domain = "test")
    {
        var bindings = new GpuBindingContract();
        var contract = new GpuShaderContract("test", [
            new ShaderStageContract("test.vsh", "test.vsh", ShaderStageKind.Vertex, bindings),
            new ShaderStageContract("test.fsh", "test.fsh", ShaderStageKind.Fragment, bindings)], 1);
        return new(domain, new(new ShaderSettings(contract)));
    }

    /// <summary>Provides one valid tightly packed position stream.</summary>
    private static VertexAttributeDesc Attribute() => new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12);

    /// <summary>Supplies deterministic capability limits without querying or creating a graphics context.</summary>
    private static GraphicsCapabilities Capabilities() => new()
    {
        Graphics33 = true, IndependentBlend = true, SampleShading = true, Tessellation = true,
        FixedIndexRestart = true, DoubleAttributes = true, DepthClamp = true,
        MaxDrawBuffers = 8, MaxVertexAttributes = 16, MaxVertexBindings = 16,
        MaxVertexStride = 2048, MaxVertexRelativeOffset = 2047, MaxSamples = 8, MaxSampleMaskWords = 2,
        MaxPatchVertices = 32, MinLineWidth = 1, MaxLineWidth = 16, MinPointSize = 1, MaxPointSize = 64
    };
    #endregion
}
