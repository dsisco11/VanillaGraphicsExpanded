using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks sparse native mutation authority, capture reuse and exact restoration on a real context.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SparseStateCoverageTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Complete admission alone neither captures native parameters nor grants arbitrary setter authority.</summary>
    [Fact]
    public void CompleteAdmissionWithoutFieldsCannotMutateNativeState()
    {
        var cache = Prepare();
#if DEBUG
        long queries = cache.BoundaryQueries, calls = cache.FixedFunctionCalls;
#endif
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("SparseEmpty",
            new PipelineStateCoverage(completeGraphics: true)), out var boundary));
        boundary!.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => cache.SetLineWidth(1));
            Assert.Throws<InvalidOperationException>(() => cache.SetStencilTestEnabled(false));
            Assert.Throws<InvalidOperationException>(() => cache.SetSampleCoverage(.5f, false));
            Assert.Throws<InvalidOperationException>(() => cache.SetSampleMask(0, uint.MaxValue));
            Assert.Throws<InvalidOperationException>(() => cache.SetDitherEnabled(false));
        });
#if DEBUG
        Assert.Equal(queries, cache.BoundaryQueries);
        Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Only selected native groups are queried, combined operands restore exactly and warm entry reuses knowledge.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SparseCapturePreservesGroupedValuesAndReusesKnownState(bool known, bool invalidate)
    {
        var cache = Prepare();
        // A function setter writes comparison, reference and value mask together. Coverage must save all three.
        GL.StencilFuncSeparate(StencilFace.Front, StencilFunction.Greater, 3, 0x57);
        GL.StencilFuncSeparate(StencilFace.Back, StencilFunction.Notequal, 4, 0x6b);
        GL.SampleCoverage(.375f, true); GL.Enable(EnableCap.Dither);
        GL.LineWidth(1); GL.Disable(EnableCap.SampleCoverage);
        for (int index = 0; index < GpuSupport.Graphics.MaxSampleMaskWords; index++) GL.SampleMask((uint)index, 0x5a5aa5a5u);
        cache.InvalidateAll(); _ = cache.MaxDrawBuffers;
        cache.SetLineWidth(1); cache.SetSampleCoverageEnabled(false);
        if (known)
        {
            cache.SetStencilFunction(StencilFace.Front, StencilFunction.Greater, 3, 0x57);
            cache.SetSampleCoverage(.375f, true); cache.SetDitherEnabled(true);
            for (int index = 0; index < GpuSupport.Graphics.MaxSampleMaskWords; index++) cache.SetSampleMask(index, 0x5a5aa5a5u);
        }
        var coverage = new PipelineStateCoverage(completeGraphics: true,
            stencil: StencilStateKnowledge.FrontStencilFunction,
            sampling: SamplingStateKnowledge.SampleCoverage, output: OutputStateKnowledge.Dither, sampleMaskWords: true);
        var declaration = new EngineBoundaryDeclaration("SparseGroups", coverage);
#if DEBUG
        long reads = cache.BoundaryQueries;
#endif
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var boundary), cache.BoundaryEntryFailure?.ToString());
#if DEBUG
        Assert.Equal(known ? 0 : 6 + GpuSupport.Graphics.MaxSampleMaskWords, cache.BoundaryQueries - reads);
#endif
        boundary!.Run(() =>
        {
            cache.SetStencilFunction(StencilFace.Front, StencilFunction.Always, 1, 0xff);
            cache.SetSampleCoverage(.75f, false); cache.SetDitherEnabled(false);
            for (int index = 0; index < GpuSupport.Graphics.MaxSampleMaskWords; index++) cache.SetSampleMask(index, 0);
            // Even a known identical value must be rejected when the operation has no authority for its group.
#if DEBUG
            long calls = cache.FixedFunctionCalls;
#endif
            Assert.Throws<InvalidOperationException>(() => cache.SetStencilFunction(StencilFace.FrontAndBack, StencilFunction.Always, 1, 0xff));
            Assert.Throws<InvalidOperationException>(() => cache.SetSampleCoverageEnabled(false));
            Assert.Throws<InvalidOperationException>(() => cache.SetLineWidth(1));
#if DEBUG
            Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
            if (invalidate) cache.Invalidate(EPipelineState.Stencil | EPipelineState.Sampling | EPipelineState.OutputInterpretation);
        });
        Assert.Equal((int)StencilFunction.Greater, GL.GetInteger(GetPName.StencilFunc));
        Assert.Equal(3, GL.GetInteger(GetPName.StencilRef)); Assert.Equal(0x57, GL.GetInteger(GetPName.StencilValueMask));
        Assert.Equal((int)StencilFunction.Notequal, GL.GetInteger(GetPName.StencilBackFunc));
        Assert.Equal(4, GL.GetInteger(GetPName.StencilBackRef)); Assert.Equal(0x6b, GL.GetInteger(GetPName.StencilBackValueMask));
        Assert.Equal(.375f, GL.GetFloat(GetPName.SampleCoverageValue)); Assert.True(GL.GetBoolean(GetPName.SampleCoverageInvert));
        Assert.True(GL.IsEnabled(EnableCap.Dither)); Assert.False(GL.IsEnabled(EnableCap.SampleCoverage));
        for (int index = 0; index < GpuSupport.Graphics.MaxSampleMaskWords; index++)
        {
            GL.GetInteger((GetIndexedPName)All.SampleMaskValue, index, out int mask);
            Assert.Equal(0x5a5aa5a5u, unchecked((uint)mask));
        }
#if DEBUG
        reads = cache.BoundaryQueries;
        long callsAfter = cache.FixedFunctionCalls;
#endif
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var warm)); warm!.Dispose();
#if DEBUG
        Assert.Equal(reads, cache.BoundaryQueries);
        Assert.Equal(callsAfter, cache.FixedFunctionCalls);
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A clear-only boundary owns its helper envelope but rejects unrelated graphics operations before mutation.</summary>
    [Fact]
    public void ClearOnlyEnvelopeClearsPixelsWithoutOwningUnrelatedGroups()
    {
        var cache = Prepare();
        using var target = CreateRenderTarget(4, 4, PixelInternalFormat.Rgba32f);
        target[0].UploadDataImmediate(Enumerable.Repeat(.25f, 64).ToArray());
        cache.SetCapability(EnableCap.ScissorTest, true); cache.SetScissor(0, 0, 1, 1);
        cache.SetColorMask(GlColorMask.FromRgba(false, false, false, false));
        try
        {
            {
    var commands=RHICommandContext.Current;
    using var scope75=commands.EnterBoundary(new("ClearOnlySparse",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope75.Run(()=>{
                commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0, Load:AttachmentLoad.Clear,
                    Clear: ColorClearValue.Float(1, .5f, .75f, 1))], area: new(1, 1, 2, 2)));
#if DEBUG
                long calls = cache.FixedFunctionCalls;
#endif
                Assert.Throws<InvalidOperationException>(() => cache.SetLineWidth(1));
                Assert.Throws<InvalidOperationException>(() => cache.SetSampleCoverage(.5f, false));
                Assert.Throws<InvalidOperationException>(() => cache.SetStencilFunction(StencilFace.Front, StencilFunction.Always, 0, 0xff));
                Assert.Throws<InvalidOperationException>(() => cache.SetLogicOperation(LogicOp.Copy));
#if DEBUG
                Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
            });
}
            Assert.True(GL.IsEnabled(EnableCap.ScissorTest));
            int[] box = new int[4]; GL.GetInteger(GetPName.ScissorBox, box); Assert.Equal(new[] { 0, 0, 1, 1 }, box);
            bool[] mask = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, 0, mask); Assert.All(mask, Assert.False);
            var pixels = target[0].ReadPixels();
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) for (int component = 0; component < 4; component++)
                Assert.Equal(x is 1 or 2 && y is 1 or 2 ? new[] { 1f, .5f, .75f, 1f }[component] : .25f,
                    pixels[(y * 4 + x) * 4 + component]);
        }
        finally
        {
            cache.SetCapability(EnableCap.ScissorTest, false); cache.SetColorMask(GlColorMask.All);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Preserve and discard loads need no clear setters even when the active boundary grants none.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonClearLoadsDoNotApplyClearState(bool discard)
    {
        var cache = Prepare();
        using var target = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        target[0].UploadDataImmediate(Enumerable.Repeat(.375f, 16).ToArray());
        var load = discard ? AttachmentLoad.Discard : AttachmentLoad.Preserve;
        var description = new RenderPassDesc(target, [new(0, load)]);
        cache.SetCapability(EnableCap.ScissorTest, true); cache.SetScissor(1, 1, 1, 1);
        cache.SetDitherEnabled(true); cache.SetDepthWriteMask(false);
        cache.SetColorMask(GlColorMask.FromRgba(false, true, false, true));
        try
        {
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("NoClearWrites",
                new PipelineStateCoverage(completeGraphics: true)), out var boundary));
#if DEBUG
            long calls = cache.FixedFunctionCalls;
#endif
            boundary!.Run(() => OpenGLRenderPassOperations.Load(description, new(0, 0, 2, 2)));
#if DEBUG
            Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
            Assert.True(GL.IsEnabled(EnableCap.ScissorTest)); Assert.True(GL.IsEnabled(EnableCap.Dither));
            Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
            bool[] mask = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, 0, mask);
            Assert.Equal(new[] { false, true, false, true }, mask);
            if (load == AttachmentLoad.Preserve) Assert.All(target[0].ReadPixels(), value => Assert.Equal(.375f, value));
        }
        finally
        {
            cache.SetCapability(EnableCap.ScissorTest, false); cache.SetColorMask(GlColorMask.All); cache.SetDepthWriteMask(true);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Indexed blend payload authority restores every combined operand and rejects a wider global alias.</summary>
    [Fact]
    public void IndexedFactorAuthorityCannotAuthorizeGlobalWrites()
    {
        var cache = Prepare();
        var inherited = new GlBlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
            BlendingFactorSrc.DstAlpha, BlendingFactorDest.OneMinusDstAlpha);
        cache.SetBlendFuncIndexed(1, inherited);
        var coverage = new PipelineStateCoverage(completeGraphics: true,
            indexedBlend: new Dictionary<int, BlendStateKnowledge> { [1] = BlendStateKnowledge.Factors | BlendStateKnowledge.Equations });
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("SparseFactors", coverage), out var boundary));
        GL.GetInteger((GetIndexedPName)GetPName.BlendEquationRgb, 1, out int rgbEquation);
        GL.GetInteger((GetIndexedPName)GetPName.BlendEquationAlpha, 1, out int alphaEquation);
        boundary!.Run(() =>
        {
#if DEBUG
            long calls = cache.FixedFunctionCalls;
#endif
            Assert.Throws<InvalidOperationException>(() => cache.SetBlendFunc(GlBlendFunc.Default));
            Assert.Throws<InvalidOperationException>(() => cache.SetBlendFuncIndexed(0, GlBlendFunc.Default));
#if DEBUG
            Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
            cache.SetBlendFuncIndexed(1, GlBlendFunc.Default);
            cache.SetBlendEquationIndexed(1, BlendEquationMode.Min, BlendEquationMode.Max);
        });
        GL.GetInteger((GetIndexedPName)GetPName.BlendSrcRgb, 1, out int srcRgb);
        GL.GetInteger((GetIndexedPName)GetPName.BlendDstRgb, 1, out int dstRgb);
        GL.GetInteger((GetIndexedPName)GetPName.BlendSrcAlpha, 1, out int srcAlpha);
        GL.GetInteger((GetIndexedPName)GetPName.BlendDstAlpha, 1, out int dstAlpha);
        Assert.Equal((int)BlendingFactorSrc.SrcAlpha, srcRgb); Assert.Equal((int)BlendingFactorDest.OneMinusSrcAlpha, dstRgb);
        Assert.Equal((int)BlendingFactorSrc.DstAlpha, srcAlpha); Assert.Equal((int)BlendingFactorDest.OneMinusDstAlpha, dstAlpha);
        GL.GetInteger((GetIndexedPName)GetPName.BlendEquationRgb, 1, out int finalRgbEquation);
        GL.GetInteger((GetIndexedPName)GetPName.BlendEquationAlpha, 1, out int finalAlphaEquation);
        Assert.Equal(rgbEquation, finalRgbEquation); Assert.Equal(alphaEquation, finalAlphaEquation);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Starts each test with unknown drawing knowledge on the registered hidden native context.</summary>
    private StateCache Prepare()
    {
        EnsureContextValid(); var cache = StateCache.Current; cache.InvalidateAll(); _ = cache.MaxDrawBuffers;
        Assert.Equal(ErrorCode.NoError, GL.GetError()); return cache;
    }
    #endregion
}
