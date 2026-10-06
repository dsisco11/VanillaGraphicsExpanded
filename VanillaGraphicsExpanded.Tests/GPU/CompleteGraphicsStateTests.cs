using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies complete state against native values independently of production migration.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CompleteGraphicsStateTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Cold external mutations, A-B-A transitions and warm repeats establish complete native state.</summary>
    [Fact]
    public void HostileStateAndAlternatingDescriptionsConverge()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current;
        var a = Pipeline(true); var b = Pipeline(false); var dynamics = Dynamics();
        GL.Enable(EnableCap.RasterizerDiscard); GL.DepthRange(.3, .7); GL.ColorMask(false, false, false, false);
        GL.BlendEquation(BlendEquationMode.Max); GL.StencilMask(0); GL.SampleCoverage(.9f, true);
        cache.InvalidateAll();
        try
        {
            cache.ApplyGraphicsState(a, dynamics); AssertNative(a);
            long calls = cache.FixedFunctionCalls, queries = cache.BoundaryQueries, errors = cache.BoundaryErrorChecks;
            cache.ApplyGraphicsState(a, dynamics);
            Assert.Equal(calls, cache.FixedFunctionCalls); Assert.Equal(queries, cache.BoundaryQueries); Assert.Equal(errors, cache.BoundaryErrorChecks);
            cache.ApplyGraphicsState(b, dynamics); AssertNative(b);
            cache.ApplyGraphicsState(a, dynamics); AssertNative(a);
            // Explicit external invalidation must force native re-establishment, even for identical intent.
            GL.Disable(EnableCap.StencilTest); GL.BlendEquation(BlendEquationMode.Max); cache.InvalidateAll();
            cache.ApplyGraphicsState(a, dynamics); AssertNative(a);
        }
        finally { cache.ApplyGraphicsState(b, dynamics); }
    }

    /// <summary>Resolved complete boundaries restore state after normal and exceptional callbacks.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompleteBoundaryRestoresEveryCategory(bool fail)
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; cache.InvalidateAll();
        var a = Pipeline(true); var b = Pipeline(false); var dynamics = Dynamics();
        cache.ApplyGraphicsState(a, dynamics);
        try
        {
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("CompleteNative", PipelineStateCoverage.From(b)), out var scope));
            var expected = new InvalidOperationException("fixture failure");
            Action operation = () => { cache.ApplyGraphicsState(b, dynamics); cache.InvalidateAll(); if (fail) throw expected; };
            if (fail) Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => scope!.Run(operation)));
            else scope!.Run(operation);
            AssertNative(a);
        }
        finally { cache.ApplyGraphicsState(b, dynamics); }
    }
    /// <summary>Observed global commands update indexed aliases and repeated observed calls are suppressed.</summary>
    [Fact]
    public void EngineGlobalAndIndexedEquationsShareKnowledge()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; cache.InvalidateAll();
        EngineStateCalls.BlendEquation(BlendEquationMode.Max);
        EngineStateCalls.BlendEquationSeparate(1, BlendEquationMode.FuncSubtract, BlendEquationMode.Min);
        long calls = cache.FixedFunctionCalls;
        EngineStateCalls.BlendEquationSeparate(1, BlendEquationMode.FuncSubtract, BlendEquationMode.Min);
        Assert.Equal(calls, cache.FixedFunctionCalls);
        EngineStateCalls.BlendEquation(BlendEquationMode.Max);
        Assert.Equal(calls + 1, cache.FixedFunctionCalls);
        for (int i = 0; i < cache.MaxDrawBuffers; i++)
        {
            GL.GetInteger((GetIndexedPName)GetPName.BlendEquationRgb, i, out int equation); Assert.Equal((int)BlendEquationMode.Max, equation);
        }
        calls = cache.FixedFunctionCalls; EngineStateCalls.BlendEquation(BlendEquationMode.Max); Assert.Equal(calls, cache.FixedFunctionCalls);
        cache.ApplyGraphicsState(Pipeline(false), Dynamics());
    }
    /// <summary>Missing draw values reject before any native transition or knowledge publication.</summary>
    [Fact]
    public void MissingDynamicsRejectBeforeMutation()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; cache.InvalidateAll();
        cache.ApplyGraphicsState(Pipeline(false), Dynamics());
        long calls = cache.FixedFunctionCalls;
        Assert.Throws<ArgumentException>(() => cache.ApplyGraphicsState(Pipeline(true), new()));
        Assert.Equal(calls, cache.FixedFunctionCalls); Assert.False(GL.IsEnabled(EnableCap.StencilTest));
    }
    /// <summary>Records driver reference clamping against actual target stencil precision.</summary>
    [Fact]
    public void StencilReferenceNativeLimits()
    {
        fixture.MakeCurrent(); int fbo = GL.GenFramebuffer(), renderbuffer = GL.GenRenderbuffer();
        try
        {
            foreach (bool attached in new[] { false, true })
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, attached ? fbo : 0);
                if (attached)
                {
                    GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbuffer);
                    GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, 8, 8);
                    GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, renderbuffer);
                }
                foreach (int reference in new[] { -1, 257 })
                {
                    GL.StencilFunc(StencilFunction.Always, reference, uint.MaxValue);
                    Assert.Equal(Math.Max(0, reference), GL.GetInteger(GetPName.StencilRef));
                }
            }
        }
        finally { GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.DeleteFramebuffer(fbo); GL.DeleteRenderbuffer(renderbuffer); GL.StencilFunc(StencilFunction.Always, 0, uint.MaxValue); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Partial equation boundaries preserve mixed indexed state through global mutations.</summary>
    [Fact]
    public void PartialBlendEquationBoundaryRestoresMixedOutputs()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; cache.InvalidateAll();
        EngineStateCalls.BlendEquation(BlendEquationMode.Max);
        EngineStateCalls.BlendEquationSeparate(1, BlendEquationMode.FuncSubtract, BlendEquationMode.Min);
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("EquationsOnly", new PipelineStateCoverage(globalBlend: BlendStateKnowledge.Equations)), out var scope));
        scope!.Run(() => { EngineStateCalls.BlendEquation(BlendEquationMode.FuncAdd); });
        GL.GetInteger((GetIndexedPName)GetPName.BlendEquationRgb, 0, out int first);
        GL.GetInteger((GetIndexedPName)GetPName.BlendEquationRgb, 1, out int second);
        Assert.Equal((int)BlendEquationMode.Max, first); Assert.Equal((int)BlendEquationMode.FuncSubtract, second);
        cache.ApplyGraphicsState(Pipeline(false), Dynamics());
    }
    /// <summary>Sampling invalidation preserves unrelated depth and forces only affected native transitions.</summary>
    [Fact]
    public void SamplingInvalidationPreservesOtherCategories()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; cache.InvalidateAll();
        cache.ApplyGraphicsState(Pipeline(false), Dynamics()); long calls = cache.FixedFunctionCalls;
        cache.Invalidate(EPipelineState.Sampling);
        cache.SetDepthFunc(DepthFunction.Less); cache.SetScissor(2, 4, 11, 13); cache.SetBlendConstant(.125f, .25f, .5f, .75f);
        Assert.Equal(calls, cache.FixedFunctionCalls);
        GL.SampleCoverage(.25f, true);
        cache.SetSampleCoverage(1, false); Assert.Equal(calls + 1, cache.FixedFunctionCalls);
        Assert.Equal(1, GL.GetFloat(GetPName.SampleCoverageValue)); Assert.False(GL.GetBoolean(GetPName.SampleCoverageInvert));
    }
    /// <summary>Insufficient declared coverage rejects the complete request before its first native mutation.</summary>
    [Fact]
    public void IncompleteBoundaryCoverageRejectsBeforeMutation()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; cache.InvalidateAll();
        var pipeline = Pipeline(false);
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Insufficient", new PipelineStateCoverage(completeGraphics: true)), out var scope));
        long calls = cache.FixedFunctionCalls;
        scope!.Run(() => Assert.Throws<InvalidOperationException>(() => cache.ApplyGraphicsState(pipeline, Dynamics())));
        Assert.Equal(calls, cache.FixedFunctionCalls);
    }
    /// <summary>Opt-in checking withdraws failed category knowledge so a later retry reaches the driver.</summary>
    [Fact]
    public void CheckedTransitionFailureDoesNotPublishKnowledge()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; cache.InvalidateAll();
        cache.SetSampleCoverage(1, false);
        try
        {
            GlDebug.CheckStateTransitions = true; GL.Enable((EnableCap)(-1));
            Assert.Throws<InvalidOperationException>(() => cache.SetSampleCoverage(.5f, false));
            long calls = cache.FixedFunctionCalls; cache.SetSampleCoverage(.5f, false);
            Assert.Equal(calls + 1, cache.FixedFunctionCalls); Assert.Equal(.5f, GL.GetFloat(GetPName.SampleCoverageValue));
        }
        finally { GlDebug.CheckStateTransitions = false; cache.SetSampleCoverage(1, false); }
    }
    /// <summary>Saved category values, knowledge and indexed masks survive live mutation and invalidation.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CategorizedSnapshotRemainsDetached(bool cold)
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; cache.InvalidateAll();
        cache.ApplyGraphicsState(Pipeline(true), Dynamics());
        if (cold) cache.InvalidateAll();
        try
        {
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("DetachedCategories", PipelineStateCoverage.From(Pipeline(false))), out var scope));
            var saved = scope!.Snapshot;
            scope.Run(() =>
            {
                cache.ApplyGraphicsState(Pipeline(false), Dynamics());
                cache.SetDepthRange(.3, .7); cache.SetSampleCoverage(.75f, false); cache.SetSampleMask(0, 0xAAu);
                cache.InvalidateAll();
                Assert.True(saved.DepthKnown.HasFlag(DepthStateKnowledge.DepthRange));
                Assert.Equal((0d, 1d), saved.Depth.DepthRange);
                Assert.True(saved.SamplingKnown.HasFlag(SamplingStateKnowledge.SampleCoverage));
                Assert.Equal((.375f, true), saved.Sampling.SampleCoverage);
                Assert.True(saved.SamplingKnown.HasFlag(SamplingStateKnowledge.SampleMask));
                Assert.True(saved.Sampling.SampleMask);
                Assert.Equal(0x55u, saved.SampleMasks.Single(entry => entry.Key == 0).Value);
                // Accessors return category values by copy, so consumers cannot corrupt restoration.
                var sampling = saved.Sampling; sampling.SampleCoverage = (0, false);
                Assert.Equal((.375f, true), saved.Sampling.SampleCoverage);
                Assert.True(saved.SamplingKnown.HasFlag(SamplingStateKnowledge.SampleCoverage));
            });
            AssertNative(Pipeline(true));
        }
        finally { cache.ApplyGraphicsState(Pipeline(false), Dynamics()); }
    }

    /// <summary>Viewport invalidation and partial snapshots preserve independently known draw parameters.</summary>
    [Fact]
    public void UnifiedDynamicKnowledgePreservesUnrelatedFields()
    {
        fixture.MakeCurrent();
        var cache = StateCache.Current;
        cache.InvalidateAll();
        cache.ApplyGraphicsState(Pipeline(false), Dynamics());
        try
        {
            cache.SetScissor(2, 4, 11, 13);
            cache.SetBlendConstant(.125f, .25f, .5f, .75f);
            cache.Invalidate(EPipelineState.Viewport);
            long calls = cache.FixedFunctionCalls, queries = cache.BoundaryQueries, checks = cache.BoundaryErrorChecks;
            cache.SetScissor(2, 4, 11, 13);
            cache.SetBlendConstant(.125f, .25f, .5f, .75f);
            Assert.Equal(calls, cache.FixedFunctionCalls);
            Assert.Equal(queries, cache.BoundaryQueries);
            Assert.Equal(checks, cache.BoundaryErrorChecks);
            // Capturing a viewport must not claim warm scissor/constant fields as restorable.
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("ViewportOnly",
                new PipelineStateCoverage(dynamic: DynamicDrawStateKnowledge.Viewport)), out var scope));
            Assert.Equal(DynamicDrawStateKnowledge.Viewport, scope!.Snapshot.DynamicKnown);
            scope.Run(() => cache.ApplyDynamic(new() { X = 0, Y = 0, Width = 8, Height = 8 }));
            int[] scissor = new int[4]; GL.GetInteger(GetPName.ScissorBox, scissor);
            Assert.Equal(new[] { 2, 4, 11, 13 }, scissor);
            cache.Invalidate(EPipelineState.ScissorRectangle);
            calls = cache.FixedFunctionCalls;
            cache.SetBlendConstant(.125f, .25f, .5f, .75f);
            cache.ApplyDynamic(Dynamics().Viewport!.Value);
            Assert.Equal(calls, cache.FixedFunctionCalls);
            cache.SetScissor(2, 4, 11, 13);
            Assert.Equal(calls + 1, cache.FixedFunctionCalls);
        }
        finally { cache.ApplyGraphicsState(Pipeline(false), Dynamics()); }
    }
    #endregion

    #region Private
    /// <summary>Defines all declared dynamic values explicitly, including distinct stencil references.</summary>
    private static GraphicsDynamicState Dynamics() => new() { Viewport = new() { X = 3, Y = 5, Width = 17, Height = 19 },
        Scissor = (2, 4, 11, 13), StencilReference = (3, 5), BlendConstant = (.125f, .25f, .5f, .75f) };

    /// <summary>Builds two independently meaningful configurations with different output and face policies.</summary>
    private static GraphicsPipelineDesc Pipeline(bool enabled)
    {
        var contract = new GpuShaderContract("complete", [new("complete.vsh", "complete.vsh", ShaderStageKind.Vertex, new()), new("complete.fsh", "complete.fsh", ShaderStageKind.Fragment, new())], 1);
        return new(new("fixture", new(new ShaderSettings(contract))), new([]), new([new(PixelInternalFormat.Srgb8Alpha8), new(PixelInternalFormat.Srgb8Alpha8)], PixelInternalFormat.Depth24Stencil8), DynamicPipelineState.All,
            depthStencil: new() { DepthTest = enabled, DepthWrite = enabled, DepthComparison = DepthFunction.Gequal, StencilTest = enabled,
                Front = new() { Comparison = StencilFunction.Equal, ReadMask = 0x35, WriteMask = 0x53, Fail = StencilOp.Incr, DepthFail = StencilOp.Decr, Pass = StencilOp.Replace },
                Back = new() { Comparison = StencilFunction.Notequal, ReadMask = 0x75, WriteMask = 0x37, Fail = StencilOp.Invert, DepthFail = StencilOp.IncrWrap, Pass = StencilOp.DecrWrap } },
            rasterizer: new() { Cull = enabled, CullMode = CullFaceMode.Front, FrontFace = FrontFaceDirection.Cw, Scissor = enabled,
                OffsetFill = enabled, DepthBiasFactor = 2, DepthBiasUnits = 3, DepthClamp = enabled, ProgramPointSize = enabled, PointSize = 3 },
            blending: [new() { Enabled = enabled, RgbEquation = BlendEquationMode.FuncSubtract, AlphaEquation = BlendEquationMode.FuncReverseSubtract, SourceRgb = BlendingFactorSrc.ConstantColor, DestinationRgb = BlendingFactorDest.OneMinusSrcAlpha, WriteGreen = !enabled }, new() { Enabled = enabled, RgbEquation = BlendEquationMode.Max, AlphaEquation = BlendEquationMode.Min, WriteAlpha = !enabled }],
            sampling: new() { Multisample = enabled, CoverageEnabled = enabled, Coverage = .375f, CoverageInvert = true, MaskEnabled = enabled, Masks = new([0x55u]), AlphaToCoverage = enabled, AlphaToOne = enabled, SampleShading = enabled, MinimumSampleShading = .5f },
            assembly: new() { Restart = enabled, RestartIndex = 17 }, output: new() { FramebufferSrgb = enabled, Dither = enabled });
    }

    /// <summary>Reads native fields rather than trusting cache storage or transition counters.</summary>
    private static void AssertNative(GraphicsPipelineDesc desc)
    {
        Assert.Equal(desc.DepthStencil.DepthTest, GL.IsEnabled(EnableCap.DepthTest));
        Assert.Equal(desc.DepthStencil.DepthWrite, GL.GetBoolean(GetPName.DepthWritemask));
        Assert.Equal((int)desc.DepthStencil.DepthComparison, GL.GetInteger(GetPName.DepthFunc));
        double[] range = new double[2]; GL.GetDouble(GetPName.DepthRange, range); Assert.Equal(new[] { 0d, 1d }, range);
        Assert.Equal(desc.DepthStencil.StencilTest, GL.IsEnabled(EnableCap.StencilTest));
        Assert.Equal((int)desc.DepthStencil.Front.Comparison, GL.GetInteger(GetPName.StencilFunc));
        Assert.Equal((int)desc.DepthStencil.Back.Comparison, GL.GetInteger(GetPName.StencilBackFunc));
        Assert.Equal((int)desc.DepthStencil.Front.WriteMask, GL.GetInteger(GetPName.StencilWritemask));
        Assert.Equal((int)desc.DepthStencil.Back.WriteMask, GL.GetInteger(GetPName.StencilBackWritemask));
        Assert.Equal((int)desc.DepthStencil.Front.Pass, GL.GetInteger(GetPName.StencilPassDepthPass));
        Assert.Equal((int)desc.DepthStencil.Back.DepthFail, GL.GetInteger(GetPName.StencilBackPassDepthFail));
        Assert.Equal(3, GL.GetInteger(GetPName.StencilRef)); Assert.Equal(5, GL.GetInteger(GetPName.StencilBackRef));
        Assert.Equal(desc.Rasterizer.Cull, GL.IsEnabled(EnableCap.CullFace)); Assert.False(GL.IsEnabled(EnableCap.RasterizerDiscard));
        Assert.Equal((int)desc.Rasterizer.FrontFace, GL.GetInteger(GetPName.FrontFace));
        Assert.Equal(desc.Rasterizer.DepthClamp, GL.IsEnabled(EnableCap.DepthClamp));
        Assert.Equal(desc.Rasterizer.OffsetFill, GL.IsEnabled(EnableCap.PolygonOffsetFill));
        Assert.Equal(desc.Rasterizer.DepthBiasFactor, GL.GetFloat(GetPName.PolygonOffsetFactor));
        Assert.Equal(desc.Rasterizer.ProgramPointSize, GL.IsEnabled(EnableCap.ProgramPointSize));
        int[] rect = new int[4]; GL.GetInteger(GetPName.Viewport, rect); Assert.Equal(new[] { 3, 5, 17, 19 }, rect);
        GL.GetInteger(GetPName.ScissorBox, rect); Assert.Equal(new[] { 2, 4, 11, 13 }, rect);
        float[] constant = new float[4]; GL.GetFloat(GetPName.BlendColor, constant); Assert.Equal(new[] { .125f, .25f, .5f, .75f }, constant);
        for (int i = 0; i < desc.Blending.Count; i++)
        {
            var blend = desc.Blending[i]; Assert.Equal(blend.Enabled, GL.IsEnabled(IndexedEnableCap.Blend, i));
            GL.GetInteger((GetIndexedPName)GetPName.BlendEquationRgb, i, out int rgb); Assert.Equal((int)blend.RgbEquation, rgb);
            GL.GetInteger((GetIndexedPName)GetPName.BlendEquationAlpha, i, out int alpha); Assert.Equal((int)blend.AlphaEquation, alpha);
            bool[] mask = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, i, mask); Assert.Equal(new[] { blend.WriteRed, blend.WriteGreen, blend.WriteBlue, blend.WriteAlpha }, mask);
        }
        Assert.Equal(desc.Sampling.Multisample, GL.IsEnabled(EnableCap.Multisample));
        Assert.Equal(desc.Sampling.CoverageEnabled, GL.IsEnabled(EnableCap.SampleCoverage));
        Assert.Equal(desc.Sampling.Coverage, GL.GetFloat(GetPName.SampleCoverageValue));
        Assert.Equal(desc.Sampling.MaskEnabled, GL.IsEnabled(EnableCap.SampleMask));
        GL.GetInteger((GetIndexedPName)All.SampleMaskValue, 0, out int word); Assert.Equal(desc.Sampling.GetMaskWord(0), unchecked((uint)word));
        Assert.Equal(desc.Sampling.AlphaToCoverage, GL.IsEnabled(EnableCap.SampleAlphaToCoverage));
        Assert.Equal(desc.Sampling.AlphaToOne, GL.IsEnabled(EnableCap.SampleAlphaToOne));
        Assert.Equal(desc.Sampling.SampleShading, GL.IsEnabled(EnableCap.SampleShading));
        Assert.Equal(desc.Sampling.MinimumSampleShading, GL.GetFloat((GetPName)All.MinSampleShadingValue));
        Assert.Equal(desc.Assembly.Restart, GL.IsEnabled(EnableCap.PrimitiveRestart));
        Assert.Equal(desc.Assembly.RestartIndex, unchecked((uint)GL.GetInteger(GetPName.PrimitiveRestartIndex)));
        Assert.Equal(desc.Output.FramebufferSrgb, GL.IsEnabled(EnableCap.FramebufferSrgb)); Assert.Equal(desc.Output.Dither, GL.IsEnabled(EnableCap.Dither));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}

