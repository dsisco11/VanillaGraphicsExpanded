using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Exercises real native restoration and failure guarantees independently of production consumer migration.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class EngineBoundaryRestorationTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Drawing state
    /// <summary>Every covered scalar/dynamic/helper field and mixed blend factors survive invalidation before cleanup.</summary>
    [Fact]
    public void AllRemainingCategoriesRestoreThroughTheirBackends()
    {
        // Arrange: Seed nondefault values for rasterizer, assembly, dynamic, clear-color, and blend categories.
        var cache = Prepare();
        cache.SetCapability(EnableCap.CullFace, true); cache.SetCapability(EnableCap.ScissorTest, true);
        cache.SetLineWidth(1); cache.SetPointSize(3);
        cache.SetProvokingVertex(ProvokingVertexMode.FirstVertexConvention); cache.SetPatchVertices(4);
        cache.ApplyDynamic(new() { X = 7, Y = 9, Width = 31, Height = 33 }); cache.SetClearColor(.2f, .4f, .6f, .8f);
        var factors = new GlBlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.Zero);
        cache.SetBlendFunc(factors); int last = cache.MaxDrawBuffers - 1; cache.SetBlendFuncIndexed(last, GlBlendFunc.Default);
        // Act: Admit the combined footprint, mutate each covered category, and invalidate it before handback.
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("AllCategories", new PipelineStateCoverage(
            rasterizer: (RasterizerStateKnowledge.ConfigurableRaster | RasterizerStateKnowledge.CullEnabled | RasterizerStateKnowledge.ScissorEnabled | RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.PointSize | RasterizerStateKnowledge.ProvokingVertex), assembly: PrimitiveAssemblyStateKnowledge.PatchVertices,
            dynamic: DynamicDrawStateKnowledge.Viewport, globalBlend: BlendStateKnowledge.Factors, clearColor: true)), out var scope));
        scope!.Run(() =>
        {
            cache.SetCapability(EnableCap.CullFace, false); cache.SetCapability(EnableCap.ScissorTest, false);
            cache.SetPointSize(1); cache.SetProvokingVertex(ProvokingVertexMode.LastVertexConvention); cache.SetPatchVertices(3);
            cache.ApplyDynamic(new() { Width = 4, Height = 4 }); cache.SetClearColor(0, 0, 0, 0); cache.SetBlendFunc(GlBlendFunc.Default);
            cache.Invalidate(EPipelineState.CullFace | EPipelineState.ScissorTest | EPipelineState.LineWidth | EPipelineState.PointSize
                | EPipelineState.ProvokingVertex | EPipelineState.PatchVertices | EPipelineState.Viewport | EPipelineState.ClearColor | EPipelineState.Blend);
        });
        // Assert: Native backends restore every captured category, including distinct global and indexed blend factors.
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
        // Arrange: Seed mixed per-output blend enable/masks and enabled depth state before declaring global coverage.
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
        // Act: Mutate global state inside the boundary and optionally invalidate all affected categories.
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope));
#if DEBUG
        long reads = cache.BoundaryQueries;
#endif
        scope!.Run(() =>
        {
            cache.SetCapability(EnableCap.DepthTest, false); cache.SetDepthWriteMask(false);
            cache.SetBlendEnabled(false); cache.SetColorMask(GlColorMask.All);
            if (invalidate) cache.Invalidate(EPipelineState.Depth | EPipelineState.Blend | EPipelineState.ColorMask);
        });
        // Assert: Both outgoing and restored native values retain exact depth and indexed output state.
        Assert.True(GL.IsEnabled(EnableCap.DepthTest)); Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i < 3, GL.IsEnabled(IndexedEnableCap.Blend, i));
            bool[] mask = new bool[4]; GL.GetBoolean(GetIndexedPName.ColorWritemask, i, mask);
            Assert.Equal(new[] { i % 2 == 0, true, true, false }, mask);
        }
#if DEBUG
        Assert.Equal(reads, cache.BoundaryQueries);
        long calls = cache.FixedFunctionCalls;
#endif
        scope.Dispose();
#if DEBUG
        Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
        cache.SetBlendEnabled(false); cache.SetColorMask(GlColorMask.All);
    }

    /// <summary>Cold and warm entry counts remain distinct from zero-call unchanged restoration.</summary>
    [Fact]
    public void NoOpRestorationAndPartialEntryCounts()
    {
        // Arrange: Start with unknown depth state so the first entry must capture the declared fields.
        var cache = Prepare();
        var declaration = new EngineBoundaryDeclaration("Depth", new PipelineStateCoverage(depth: (DepthStateKnowledge.TestEnabled | DepthStateKnowledge.Comparison | DepthStateKnowledge.WriteEnabled)));
#if DEBUG
        long reads = cache.BoundaryQueries, calls = cache.FixedFunctionCalls;
#endif
        // Act: Compare cold entry, warm entry, and partial re-entry after losing depth knowledge.
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var cold));
        cold!.Dispose();
#if DEBUG
        // Assert: Cold entry reads the three unknown fields without making restoration calls for unchanged values.
        Assert.Equal(3, cache.BoundaryQueries - reads);
        Assert.Equal(calls, cache.FixedFunctionCalls);
        reads = cache.BoundaryQueries;
        long checks = cache.BoundaryErrorChecks;
#endif
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var warm)); warm!.Dispose();
#if DEBUG
        // Assert: Warm entry is query-free and its unchanged restoration adds no driver calls or error checks.
        Assert.Equal(reads, cache.BoundaryQueries);
        Assert.Equal(calls, cache.FixedFunctionCalls);
        Assert.Equal(0, cache.BoundaryErrorChecks - checks);
#endif
        cache.Invalidate(EPipelineState.Depth); cache.SetDepthFunc(DepthFunction.Less);
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var partial)); partial!.Dispose();
#if DEBUG
        // Assert: Losing depth knowledge requires reads only for the two missing fields.
        Assert.Equal(2, cache.BoundaryQueries - reads);
#endif
    }

    /// <summary>External effects update targeted knowledge without managed admission vetoes or rejection guesses.</summary>
    [Fact]
    public void ExternalOperationsPublishTargetedKnowledgeWithoutVetoes()
    {
        // Arrange: Seed known depth and point-size state before exercising external calls within an empty footprint.
        var cache = Prepare();
        cache.SetDepthFunc(DepthFunction.Greater); cache.SetPointSize(2);
        GL.DepthFunc(DepthFunction.Less);
#if DEBUG
        long calls = cache.FixedFunctionCalls;
#endif
        cache.SetPointSize(2);
#if DEBUG
        Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
        cache.SetDepthFunc(DepthFunction.Greater);
#if DEBUG
        Assert.Equal(calls + 1, cache.FixedFunctionCalls);
#endif
        // Act: Make native depth changes, including a rejected comparison, then attempt an uncovered managed write.
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Empty"), out var scope));
        try
        {
            // Assert: Native changes execute; rejected native state preserves the prior value and managed mutation is vetoed.
            GL.DepthMask(false);
            Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
            // OpenGL rejects an invalid comparison; observation preserves the prior established value.
            GL.DepthFunc((DepthFunction)(-1));
            Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
            Assert.Equal((int)DepthFunction.Greater, GL.GetInteger(GetPName.DepthFunc));
            Assert.Throws<InvalidOperationException>(() => cache.SetDepthWriteMask(true));
        }
        finally { scope!.Dispose(); }
#if DEBUG
        calls = cache.FixedFunctionCalls;
#endif
        cache.SetDepthFunc(DepthFunction.Greater);
#if DEBUG
        Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
    }
    #endregion

    #region Failures and lifetime
    /// <summary>Operation code cannot end its boundary early or nest execution on the same scope.</summary>
    [Fact]
    public void RunOwnsTheCleanupLifetime()
    {
        // Arrange: Enter one scope and transfer callback execution to its Run method.
        var cache = Prepare(); Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Lifetime"), out var scope));
        // Act and assert: Attempts to nest Run or dispose from inside the callback are rejected until Run completes.
        scope!.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => scope.Run(() => { }));
            Assert.Throws<InvalidOperationException>(() => scope.Dispose());
        });
        // Assert: Once Run returns, the owning caller can dispose the completed boundary.
        scope.Dispose();
    }
    /// <summary>Setup/draw errors and multiple cleanup failures survive together; independent cleanup continues in order.</summary>
    [Theory]
    [InlineData("setup")]
    [InlineData("draw")]
    public void OrderedCleanupPreservesOperationAndAllFailures(string point)
    {
        // Arrange: Register three cleanup owners whose reverse order includes two controlled failures.
        var cache = Prepare(); cache.SetDepthWriteMask(true);
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Failure",
            new PipelineStateCoverage(depth: DepthStateKnowledge.WriteEnabled)), out var scope));
        var order = new List<string>();
        scope!.AddCleanup(EngineBoundaryCleanup.Framebuffers, new Cleanup(() => order.Add("framebuffer")));
        scope.AddCleanup(EngineBoundaryCleanup.Bindings, new Cleanup(() => { order.Add("bindings"); throw new Exception("binding failure"); }));
        scope.AddCleanup(EngineBoundaryCleanup.Shader, new Cleanup(() => { order.Add("shader"); throw new Exception("shader failure"); }));
        var operation = new InvalidOperationException(point);
        // Act: Mutate depth and throw from the operation while cleanup owners run in reverse registration order.
        var result = Assert.Throws<AggregateException>(() => scope.Run(() => { cache.SetDepthWriteMask(false); throw operation; }));
        // Assert: Operation and restoration failures are retained, all cleanups run, and depth is restored.
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
        // Arrange: Admit depth-write restoration from an enabled mask.
        var cache = Prepare(); cache.SetDepthWriteMask(true);
        var declaration = new EngineBoundaryDeclaration("Operation", new PipelineStateCoverage(depth: DepthStateKnowledge.WriteEnabled));
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope));
        var expected = new InvalidOperationException("draw");
        // Act: Change the mask, then throw the expected operation exception inside Run.
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => scope!.Run(() => { cache.SetDepthWriteMask(false); throw expected; })));
        // Assert: Successful handback preserves the original exception and permits a subsequent boundary.
        Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var next)); next!.Dispose();
    }

    /// <summary>Driver errors fail handoff and withdraw exactly the affected field instead of reporting cached success.</summary>
    [Fact]
    public void NativeRestorationFailureLeavesFieldUnknown()
    {
        // Arrange: Admit depth-mask restoration and retain known point-size state as an unrelated control.
        var cache = Prepare(); cache.SetDepthWriteMask(true); cache.SetPointSize(2);
        var declaration = new EngineBoundaryDeclaration("NativeFailure", new PipelineStateCoverage(depth: DepthStateKnowledge.WriteEnabled));
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope));
        cache.SetDepthWriteMask(false);
        GL.Enable((EnableCap)(-1));
        // Act: Change depth, queue a native error, and dispose so the restoration check fails at handback.
        Assert.Throws<EngineBoundaryRestoreException>(() => scope!.Dispose());
        // Assert: Only the affected depth field becomes unknown; unrelated cache knowledge remains usable.
        var known = (DepthStateKnowledge)typeof(OpenGlStateCache).GetField("depthKnown", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache)!;
        Assert.False(known.HasFlag(DepthStateKnowledge.WriteEnabled));
#if DEBUG
        long calls = cache.FixedFunctionCalls;
#endif
        cache.SetPointSize(2);
#if DEBUG
        Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
        cache.SetDepthWriteMask(true);
#if DEBUG
        Assert.Equal(calls + 1, cache.FixedFunctionCalls);
#endif
        scope!.Dispose();
#if DEBUG
        Assert.Equal(calls + 1, cache.FixedFunctionCalls);
#endif
    }

    /// <summary>Explicit invalidation does not cancel an active scope or skip its cleanup owners.</summary>
    [Fact]
    public void InvalidationPreservesCleanupAndRestoration()
    {
        // Arrange: Register one cleanup owner and retain the active scope before invalidating the cache.
        var cache = Prepare();
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Invalidation"), out var scope));
        int cleaned = 0;
        scope!.AddCleanup(EngineBoundaryCleanup.Shader, new Cleanup(() => cleaned++));
        // Act: Invalidate mutable state, dispose twice, then enter and release a subsequent boundary.
        cache.InvalidateAll();
        // Assert: Active cleanup runs once and invalidation does not strand the next entry.
        Assert.Same(cache, OpenGlStateCache.Current);
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
    private OpenGlStateCache Prepare()
    {
        fixture.MakeCurrent(); var cache = OpenGlStateCache.Current; cache.InvalidateAll(); _ = cache.MaxDrawBuffers;
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
