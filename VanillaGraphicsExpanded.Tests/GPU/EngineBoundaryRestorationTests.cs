using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises real native restoration and failure guarantees independently of production consumer migration.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineBoundaryRestorationTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Drawing state
    /// <summary>Every covered scalar/dynamic/helper field and mixed blend factors survive invalidation before cleanup.</summary>
    [Fact]
    public void AllRemainingCategoriesRestoreThroughTheirBackends()
    {
        var cache = Prepare();
        cache.SetCapability(EnableCap.CullFace, true); cache.SetCapability(EnableCap.ScissorTest, true);
        cache.SetLineWidth(1); cache.SetPointSize(3);
        cache.SetProvokingVertex(ProvokingVertexMode.FirstVertexConvention); cache.SetPatchVertices(4);
        cache.ApplyDynamic(new() { X = 7, Y = 9, Width = 31, Height = 33 }); cache.SetClearColor(.2f, .4f, .6f, .8f);
        var factors = new GlBlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.Zero);
        cache.SetBlendFunc(factors); int last = cache.MaxDrawBuffers - 1; cache.SetBlendFuncIndexed(last, GlBlendFunc.Default);
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("AllCategories", new PipelineStateCoverage(
            rasterizer: RasterizerStateKnowledge.All, assembly: PrimitiveAssemblyStateKnowledge.All,
            dynamic: DynamicDrawStateKnowledge.All, globalBlend: BlendStateKnowledge.Factors, clearColor: true)), out var scope));
        scope!.Run(() =>
        {
            cache.SetCapability(EnableCap.CullFace, false); cache.SetCapability(EnableCap.ScissorTest, false);
            cache.SetPointSize(1); cache.SetProvokingVertex(ProvokingVertexMode.LastVertexConvention); cache.SetPatchVertices(3);
            cache.ApplyDynamic(new() { Width = 4, Height = 4 }); cache.SetClearColor(0, 0, 0, 0); cache.SetBlendFunc(GlBlendFunc.Default);
            cache.Invalidate(EPipelineState.CullFace | EPipelineState.ScissorTest | EPipelineState.LineWidth | EPipelineState.PointSize
                | EPipelineState.ProvokingVertex | EPipelineState.PatchVertices | EPipelineState.Viewport | EPipelineState.ClearColor | EPipelineState.Blend);
        });
        Assert.True(GL.IsEnabled(EnableCap.CullFace)); Assert.True(GL.IsEnabled(EnableCap.ScissorTest));
        Assert.Equal(1, GL.GetFloat(GetPName.LineWidth)); Assert.Equal(3, GL.GetFloat(GetPName.PointSize));
        Assert.Equal((int)ProvokingVertexMode.FirstVertexConvention, GL.GetInteger(GetPName.ProvokingVertex));
        Assert.Equal(4, GL.GetInteger(GetPName.PatchVertices));
        int[] viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport); Assert.Equal(new[] { 7, 9, 31, 33 }, viewport);
        float[] clear = new float[4]; GL.GetFloat(GetPName.ColorClearValue, clear); Assert.Equal(new[] { .2f, .4f, .6f, .8f }, clear);
        GL.GetInteger((GetIndexedPName)GetPName.BlendSrcRgb, 0, out int firstFactor);
        GL.GetInteger((GetIndexedPName)GetPName.BlendSrcRgb, last, out int lastFactor);
        Assert.Equal((int)BlendingFactorSrc.SrcAlpha, firstFactor); Assert.Equal((int)BlendingFactorSrc.One, lastFactor);
        cache.SetCapability(EnableCap.ScissorTest, false); cache.SetCapability(EnableCap.CullFace, false);
    }
    /// <summary>Mixed output state returns to its exact values without flattening metadata slots.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedOutputsAndDepthRestoreAfterMutation(bool invalidate)
    {
        var cache = Prepare();
        int count = cache.MaxDrawBuffers;
        cache.SetCapability(EnableCap.DepthTest, true); cache.SetDepthWriteMask(true);
        for (int i = 0; i < count; i++)
        {
            cache.SetBlendEnabledIndexed(i, i < 3);
            cache.SetColorMaskIndexed(i, GlColorMask.FromRgba(i % 2 == 0, true, true, false));
        }
        var declaration = new EngineBoundaryDeclaration("Mixed", new PipelineStateCoverage(
            depth: DepthStateKnowledge.TestEnabled | DepthStateKnowledge.WriteEnabled,
            globalBlend: BlendStateKnowledge.Enabled | BlendStateKnowledge.WriteMask));
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope));
        long reads = cache.BoundaryQueries;
        scope!.Run(() =>
        {
            cache.SetCapability(EnableCap.DepthTest, false); cache.SetDepthWriteMask(false);
            cache.SetBlendEnabled(false); cache.SetColorMask(GlColorMask.All);
            if (invalidate) cache.Invalidate(EPipelineState.Depth | EPipelineState.Blend | EPipelineState.ColorMask);
        });
        Assert.True(GL.IsEnabled(EnableCap.DepthTest)); Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i < 3, GL.IsEnabled(IndexedEnableCap.Blend, i));
            bool[] mask = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, i, mask);
            Assert.Equal(new[] { i % 2 == 0, true, true, false }, mask);
        }
        Assert.Equal(reads, cache.BoundaryQueries);
        long calls = cache.FixedFunctionCalls;
        scope.Dispose(); Assert.Equal(calls, cache.FixedFunctionCalls);
        cache.SetBlendEnabled(false); cache.SetColorMask(GlColorMask.All);
    }

    /// <summary>Cold and warm entry counts remain distinct from zero-call unchanged restoration.</summary>
    [Fact]
    public void NoOpRestorationAndPartialEntryCounts()
    {
        var cache = Prepare();
        var declaration = new EngineBoundaryDeclaration("Depth", new PipelineStateCoverage(depth: DepthStateKnowledge.All));
        long reads = cache.BoundaryQueries, calls = cache.FixedFunctionCalls;
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var cold));
        cold!.Dispose();
        Assert.Equal(3, cache.BoundaryQueries - reads); Assert.Equal(calls, cache.FixedFunctionCalls);
        reads = cache.BoundaryQueries;
        long checks = cache.BoundaryErrorChecks;
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var warm)); warm!.Dispose();
        Assert.Equal(reads, cache.BoundaryQueries); Assert.Equal(calls, cache.FixedFunctionCalls);
        Assert.Equal(0, cache.BoundaryErrorChecks - checks);
        cache.Invalidate(EPipelineState.Depth); cache.SetDepthFunc(DepthFunction.Less);
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var partial)); partial!.Dispose();
        Assert.Equal(2, cache.BoundaryQueries - reads);
    }

    /// <summary>Unknown external mutations require ending managed authority and invalidate only declared categories.</summary>
    [Fact]
    public void ExternalOperationsWithdrawTargetedKnowledgeEvenOnFailure()
    {
        var cache = Prepare();
        cache.SetDepthFunc(DepthFunction.Greater); cache.SetPointSize(2);
        var failure = new InvalidOperationException("external");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => cache.ExecuteExternal(EPipelineState.Depth, () =>
        { GL.DepthFunc(DepthFunction.Less); throw failure; })));
        long calls = cache.FixedFunctionCalls;
        cache.SetPointSize(2); Assert.Equal(calls, cache.FixedFunctionCalls);
        cache.SetDepthFunc(DepthFunction.Greater); Assert.Equal(calls + 1, cache.FixedFunctionCalls);
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Empty"), out var scope));
        try { Assert.Throws<InvalidOperationException>(() => cache.ExecuteExternal(EPipelineState.Depth, () => GL.DepthMask(false))); }
        finally { scope!.Dispose(); }
    }
    #endregion

    #region Failures and lifetime
    /// <summary>Operation code cannot end its boundary early or nest execution on the same scope.</summary>
    [Fact]
    public void RunOwnsTheCleanupLifetime()
    {
        var cache = Prepare(); Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Lifetime"), out var scope));
        scope!.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => scope.Run(() => { }));
            Assert.Throws<InvalidOperationException>(() => scope.Dispose());
        });
        scope.Dispose();
    }
    /// <summary>Setup/draw errors and multiple cleanup failures survive together; independent cleanup continues in order.</summary>
    [Theory]
    [InlineData("setup")]
    [InlineData("draw")]
    public void OrderedCleanupPreservesOperationAndAllFailures(string point)
    {
        var cache = Prepare(); cache.SetDepthWriteMask(true);
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Failure",
            new PipelineStateCoverage(depth: DepthStateKnowledge.WriteEnabled)), out var scope));
        var order = new List<string>();
        scope!.AddCleanup(EngineBoundaryCleanup.Framebuffers, new Cleanup(() => order.Add("framebuffer")));
        scope.AddCleanup(EngineBoundaryCleanup.Bindings, new Cleanup(() => { order.Add("bindings"); throw new Exception("binding failure"); }));
        scope.AddCleanup(EngineBoundaryCleanup.Shader, new Cleanup(() => { order.Add("shader"); throw new Exception("shader failure"); }));
        var operation = new InvalidOperationException(point);
        var result = Assert.Throws<AggregateException>(() => scope.Run(() => { cache.SetDepthWriteMask(false); throw operation; }));
        Assert.Same(operation, result.InnerExceptions[0]);
        Assert.True(EngineBoundaryRestoreException.IsRestorationFailure(result));
        Assert.Equal(new[] { "shader", "bindings", "framebuffer" }, order);
        Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
        scope.Dispose(); Assert.Equal(3, order.Count);
    }

    /// <summary>A successful handoff preserves the original operation exception and allows subsequent entry.</summary>
    [Fact]
    public void OperationFailureRestoresAndPreservesOriginalException()
    {
        var cache = Prepare(); cache.SetDepthWriteMask(true);
        var declaration = new EngineBoundaryDeclaration("Operation", new PipelineStateCoverage(depth: DepthStateKnowledge.WriteEnabled));
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope));
        var expected = new InvalidOperationException("draw");
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => scope!.Run(() => { cache.SetDepthWriteMask(false); throw expected; })));
        Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var next)); next!.Dispose();
    }

    /// <summary>Driver errors fail handoff and withdraw exactly the affected field instead of reporting cached success.</summary>
    [Fact]
    public void NativeRestorationFailureLeavesFieldUnknown()
    {
        var cache = Prepare(); cache.SetDepthWriteMask(true); cache.SetPointSize(2);
        var declaration = new EngineBoundaryDeclaration("NativeFailure", new PipelineStateCoverage(depth: DepthStateKnowledge.WriteEnabled));
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope));
        cache.SetDepthWriteMask(false);
        GL.Enable((EnableCap)(-1));
        Assert.Throws<EngineBoundaryRestoreException>(() => scope!.Dispose());
        var known = (DepthStateKnowledge)typeof(StateCache).GetField("depthKnown", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache)!;
        Assert.False(known.HasFlag(DepthStateKnowledge.WriteEnabled));
        long calls = cache.FixedFunctionCalls;
        cache.SetPointSize(2); Assert.Equal(calls, cache.FixedFunctionCalls);
        cache.SetDepthWriteMask(true); Assert.Equal(calls + 1, cache.FixedFunctionCalls);
        scope!.Dispose(); Assert.Equal(calls + 1, cache.FixedFunctionCalls);
    }

    /// <summary>Explicit invalidation does not cancel an active scope or skip its cleanup owners.</summary>
    [Fact]
    public void InvalidationPreservesCleanupAndRestoration()
    {
        var cache = Prepare();
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Invalidation"), out var scope));
        int cleaned = 0;
        scope!.AddCleanup(EngineBoundaryCleanup.Shader, new Cleanup(() => cleaned++));
        cache.InvalidateAll();
        Assert.Same(cache, StateCache.Current);
        scope.Dispose();
        Assert.Equal(1, cleaned);
        scope.Dispose();
        Assert.Equal(1, cleaned);
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Next"), out var next));
        next!.Dispose();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Establishes a registered native context without stale mutable knowledge.</summary>
    private StateCache Prepare()
    {
        fixture.MakeCurrent(); var cache = StateCache.Current; cache.InvalidateAll(); _ = cache.MaxDrawBuffers;
        Assert.Equal(ErrorCode.NoError, GL.GetError()); return cache;
    }
    #endregion

    /// <summary>Models an independent cleanup owner including controlled failures.</summary>
    private sealed class Cleanup(Action cleanup) : IDisposable
    {
        /// <summary>Runs the supplied ownership cleanup.</summary>
        public void Dispose() => cleanup();
    }
}
