using System.Reflection;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ErrorCode = OpenTK.Graphics.OpenGL.ErrorCode;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies declared entry without depending on restoration or production capture migration.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineBoundaryEntryTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Resolution and ownership
    /// <summary>Cold, warm and partially invalidated entries resolve exactly their missing fields.</summary>
    [Fact]
    public void ColdWarmAndPartialEntryHaveExactQueryCoverage()
    {
        var cache = Prepare();
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Greater);
        GL.DepthMask(false);
        var declaration = new EngineBoundaryDeclaration("Depth", new PipelineStateCoverage(depth: DepthStateKnowledge.All));
        long queries = cache.BoundaryQueries;
        long calls = cache.FixedFunctionCalls;
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var first), cache.BoundaryEntryFailure?.ToString());
        Assert.Equal(3, cache.BoundaryQueries - queries);
        Assert.Equal(calls, cache.FixedFunctionCalls);
        Assert.True(first!.Snapshot.Depth.TestEnabled);
        Assert.Equal(DepthFunction.Greater, first.Snapshot.Depth.Comparison);
        Assert.False(first.Snapshot.Depth.WriteEnabled);
        cache.ReleaseEngineBoundary(first);
        queries = cache.BoundaryQueries;
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var warm));
        Assert.Equal(queries, cache.BoundaryQueries);
        cache.ReleaseEngineBoundary(warm!);
        cache.Invalidate(EPipelineState.Depth);
        cache.SetDepthFunc(DepthFunction.Less);
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var partial));
        Assert.Equal(2, cache.BoundaryQueries - queries);
        Assert.Equal(DepthFunction.Less, partial!.Snapshot.Depth.Comparison);
        cache.ReleaseEngineBoundary(partial);
    }

    /// <summary>Every categorized field, dynamic viewport and clear helper resolve to actual incoming values.</summary>
    [Fact]
    public void AllCategoriesCaptureConcreteNativeValues()
    {
        var cache = Prepare();
        GL.Disable(EnableCap.CullFace); GL.Enable(EnableCap.ScissorTest);
        GL.LineWidth(1); GL.PointSize(3);
        GL.ProvokingVertex(ProvokingVertexMode.FirstVertexConvention);
        GL.PatchParameter(PatchParameterInt.PatchVertices, 4);
        GL.Viewport(7, 9, 31, 37); GL.ClearColor(.2f, .4f, .6f, .8f);
        var declaration = new EngineBoundaryDeclaration("All", new PipelineStateCoverage(
            rasterizer: RasterizerStateKnowledge.All, assembly: PrimitiveAssemblyStateKnowledge.All,
            dynamic: DynamicDrawStateKnowledge.All, clearColor: true));
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope), cache.BoundaryEntryFailure?.ToString());
        try
        {
            var saved = scope!.Snapshot;
            Assert.False(saved.Rasterizer.CullEnabled); Assert.True(saved.Rasterizer.ScissorEnabled);
            Assert.Equal(1, saved.Rasterizer.LineWidth); Assert.Equal(3, saved.Rasterizer.PointSize);
            Assert.Equal(ProvokingVertexMode.FirstVertexConvention, saved.Rasterizer.ProvokingVertex);
            Assert.Equal(4, saved.Assembly.PatchVertices);
            Assert.Equal(7, saved.Dynamic.X); Assert.Equal(9, saved.Dynamic.Y);
            Assert.Equal(31, saved.Dynamic.Width); Assert.Equal(37, saved.Dynamic.Height);
            Assert.Equal(new Vector4(.2f, .4f, .6f, .8f), saved.ClearColor);
            long queries = cache.BoundaryQueries, capabilities = GpuSupport.CaptureCount;
            cache.ApplyDynamic(new DynamicDrawState { Width = 11, Height = 13 });
            cache.SetPatchVertices(3);
            Assert.Equal(queries, cache.BoundaryQueries);
            Assert.Equal(capabilities, GpuSupport.CaptureCount);
        }
        finally { cache.ReleaseEngineBoundary(scope!); GL.Disable(EnableCap.ScissorTest); cache.InvalidateAll(); }
    }

    /// <summary>Global coverage includes unrouted outputs and independently retains mixed values after mutation.</summary>
    [Fact]
    public void MixedIndexedSnapshotSurvivesMutationAndInvalidation()
    {
        var cache = Prepare();
        int count = cache.MaxDrawBuffers;
        var factors = new GlBlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
            BlendingFactorSrc.One, BlendingFactorDest.Zero);
        for (int i = 0; i < count; i++)
        {
            if (i < 3) GL.Enable(IndexedEnableCap.Blend, i); else GL.Disable(IndexedEnableCap.Blend, i);
            GL.BlendFuncSeparate(i, factors.SrcRgb, factors.DstRgb, factors.SrcAlpha, factors.DstAlpha);
            GL.ColorMask(i, i % 2 == 0, true, false, true);
        }
        var declaration = new EngineBoundaryDeclaration("Mixed", new PipelineStateCoverage(globalBlend: BlendStateKnowledge.All));
        long queries = cache.BoundaryQueries;
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope), cache.BoundaryEntryFailure?.ToString());
        try
        {
            Assert.Equal(count * 8, cache.BoundaryQueries - queries);
            cache.SetBlendEnabled(false); cache.SetBlendFunc(GlBlendFunc.Default); cache.SetColorMask(GlColorMask.All);
            cache.Invalidate(EPipelineState.Blend | EPipelineState.ColorMask);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(BlendStateKnowledge.All, scope!.Snapshot.Coverage.BlendAt(i));
                var saved = scope.Snapshot.BlendAt(i);
                Assert.Equal(i < 3, saved.Enabled); Assert.Equal(factors, saved.Factors);
                Assert.Equal(GlColorMask.FromRgba(i % 2 == 0, true, false, true), saved.WriteMask);
            }
        }
        finally { cache.ReleaseEngineBoundary(scope!); cache.SetColorMask(GlColorMask.All); }
    }

    /// <summary>Category aggregation never promotes unknown, uncovered fields into captured defaults.</summary>
    [Fact]
    public void NarrowCoverageKeepsUncapturedFieldsUnknown()
    {
        var cache = Prepare();
        var declaration = new EngineBoundaryDeclaration("WriteOnly", new PipelineStateCoverage(depth: DepthStateKnowledge.WriteEnabled));
        long queries = cache.BoundaryQueries;
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope));
        try
        {
            Assert.Equal(1, cache.BoundaryQueries - queries);
            Assert.Equal(DepthStateKnowledge.WriteEnabled, scope!.Snapshot.Coverage.Depth);
            var known = (DepthStateKnowledge)typeof(StateCache).GetField("depthKnown", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache)!;
            Assert.Equal(DepthStateKnowledge.WriteEnabled, known);
            cache.SetDepthWriteMask(!scope.Snapshot.Depth.WriteEnabled);
            Assert.Throws<InvalidOperationException>(() => cache.SetDepthFunc(DepthFunction.Less));
        }
        finally { cache.ReleaseEngineBoundary(scope!); }
    }
    #endregion

    #region Rejection and failure
    /// <summary>An undeclared late PSO field is rejected before earlier covered fields change.</summary>
    [Fact]
    public void WholePipelineIsValidatedBeforeFirstTransition()
    {
        var cache = Prepare();
        cache.SetCapability(EnableCap.DepthTest, true);
        var declaration = new EngineBoundaryDeclaration("DepthEnable", new PipelineStateCoverage(depth: DepthStateKnowledge.TestEnabled));
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope));
        try
        {
            var bits = GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable) | GlPipelineStateMask.From(GlPipelineStateId.ColorMask);
            var pipeline = new GlPipelineDesc(bits, default);
            long calls = cache.FixedFunctionCalls;
            Assert.Throws<InvalidOperationException>(() => cache.Apply(pipeline));
            Assert.Equal(calls, cache.FixedFunctionCalls); Assert.True(GL.IsEnabled(EnableCap.DepthTest));
        }
        finally { cache.ReleaseEngineBoundary(scope!); }
    }

    /// <summary>Every scalar/backend adapter rejects undeclared effects even when its value is already known.</summary>
    [Fact]
    public void BackendAndEngineMutationsCannotBypassCoverage()
    {
        var cache = Prepare();
        cache.SetDepthFunc(DepthFunction.Less);
        cache.SetLineWidth(1);
        cache.SetColorMask(GlColorMask.All);
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Empty"), out var scope));
        try
        {
            Action[] operations = [
                () => EngineStateCalls.Enable(EnableCap.DepthTest), () => EngineStateCalls.Disable(EnableCap.CullFace),
                () => EngineStateCalls.Disable(EnableCap.ScissorTest), () => cache.SetDepthFunc(DepthFunction.Less),
                () => cache.SetDepthWriteMask(true), () => cache.SetLineWidth(1), () => cache.SetPointSize(1),
                () => cache.SetProvokingVertex(ProvokingVertexMode.LastVertexConvention), () => cache.SetPatchVertices(3),
                () => EngineStateCalls.Viewport(0, 0, 16, 16), () => EngineStateCalls.ClearColor(0, 0, 0, 0),
                () => cache.SetBlendEnabled(false), () => cache.SetBlendEnabledIndexed(0, false),
                () => cache.SetBlendFunc(GlBlendFunc.Default), () => cache.SetBlendFuncIndexed(0, GlBlendFunc.Default),
                () => cache.SetColorMask(GlColorMask.All), () => EngineStateCalls.ColorMask(0, true, true, true, true),
                () => EngineStateCalls.Enable(EnableCap.StencilTest),
                () => EngineStateCalls.Enable((IndexedEnableCap)EnableCap.ScissorTest, 0),
                () => EngineStateCalls.PatchParameter((PatchParameterInt)(-1), 3),
                () => cache.CaptureLegacyFixedFunctionState(), () => { _ = cache.PatchVertices; },
                () => { _ = cache.ProvokingVertex; }
            ];
            long calls = cache.FixedFunctionCalls, queries = cache.BoundaryQueries;
            foreach (var operation in operations) Assert.Throws<InvalidOperationException>(operation);
            Assert.Equal(calls, cache.FixedFunctionCalls); Assert.Equal(queries, cache.BoundaryQueries);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { cache.ReleaseEngineBoundary(scope!); }
    }

    /// <summary>Indexed authorization does not permit a global alias or a different output's mutation.</summary>
    [Fact]
    public void IndexedCoverageRejectsGlobalAliasesAndUnlistedIndices()
    {
        var cache = Prepare();
        var coverage = new PipelineStateCoverage(indexedBlend: new Dictionary<int, BlendStateKnowledge> { [0] = BlendStateKnowledge.All });
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("OneOutput", coverage), out var scope));
        try
        {
            long calls = cache.FixedFunctionCalls;
            Assert.Throws<InvalidOperationException>(() => cache.SetBlendEnabled(false));
            Assert.Throws<InvalidOperationException>(() => cache.SetBlendFunc(GlBlendFunc.Default));
            Assert.Throws<InvalidOperationException>(() => cache.SetColorMask(GlColorMask.All));
            Assert.Throws<InvalidOperationException>(() => cache.SetBlendEnabledIndexed(1, false));
            Assert.Equal(calls, cache.FixedFunctionCalls);
            cache.SetBlendEnabledIndexed(0, !scope!.Snapshot.BlendAt(0).Enabled);
            Assert.Equal(calls + 1, cache.FixedFunctionCalls);
            Assert.Equal(BlendStateKnowledge.None, scope.Snapshot.Coverage.BlendAt(1));
        }
        finally { cache.ReleaseEngineBoundary(scope!); }
    }

    /// <summary>Entry refuses native query errors without publishing default values or an active token.</summary>
    [Fact]
    public void QueryErrorLeavesNoScopeAndNoDrawingStateChange()
    {
        var cache = Prepare();
        GL.DepthMask(false);
        GL.Enable((EnableCap)(-1)); // Deterministic native error before the checked incoming read.
        long calls = cache.FixedFunctionCalls;
        var declaration = new EngineBoundaryDeclaration("Failure", new PipelineStateCoverage(depth: DepthStateKnowledge.WriteEnabled));
        Assert.False(cache.TryBeginEngineBoundary(declaration, out var failed));
        Assert.Null(failed); Assert.NotNull(cache.BoundaryEntryFailure);
        Assert.Equal(calls, cache.FixedFunctionCalls); Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
        var known = (DepthStateKnowledge)typeof(StateCache).GetField("depthKnown", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache)!;
        Assert.Equal(DepthStateKnowledge.None, known);
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var valid));
        Assert.False(valid!.Snapshot.Depth.WriteEnabled); cache.ReleaseEngineBoundary(valid);
    }

    /// <summary>A native read that returns a default alongside an error is never accepted as a resolved value.</summary>
    [Fact]
    public void NativeReadFailureRejectsReturnedDefault()
    {
        var cache = Prepare();
        // Exercise the post-read error path with a real failing GL query, without a production test hook.
        var query = typeof(StateCache).GetMethod("QueryBoundary", BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(int));
        long calls = cache.FixedFunctionCalls;
        long reads = cache.BoundaryQueries;
        var failure = Assert.Throws<TargetInvocationException>(() => query.Invoke(cache,
            [new Func<int>(() => GL.GetInteger((GetPName)(-1)))]));
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal(reads + 1, cache.BoundaryQueries);
        Assert.Equal(calls, cache.FixedFunctionCalls);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Nested entry and unsupported indices fail before any mutation or additional state query.</summary>
    [Fact]
    public void NestedAndInvalidOutputEntryAreRejected()
    {
        var cache = Prepare();
        var declaration = new EngineBoundaryDeclaration("Outer");
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope));
        try
        {
            long queries = cache.BoundaryQueries;
            Assert.Throws<InvalidOperationException>(() => cache.TryBeginEngineBoundary(declaration, out _));
            Assert.Equal(queries, cache.BoundaryQueries);
        }
        finally { cache.ReleaseEngineBoundary(scope!); }
        var invalid = new PipelineStateCoverage(indexedBlend: new Dictionary<int, BlendStateKnowledge> { [cache.MaxDrawBuffers] = BlendStateKnowledge.Enabled });
        Assert.False(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("Invalid", invalid), out var failed));
        Assert.Null(failed);
    }

    /// <summary>Entry requires a live registered context before any state queries or mutations begin.</summary>
    [Fact]
    public unsafe void MissingCurrentContextRejectsEntry()
    {
        var cache = Prepare();
        var declaration = new EngineBoundaryDeclaration("Context", new PipelineStateCoverage(depth: DepthStateKnowledge.WriteEnabled));
        try
        {
            GLFW.MakeContextCurrent(null);
            long queries = cache.BoundaryQueries;
            Assert.False(cache.TryBeginEngineBoundary(declaration, out var failed));
            Assert.Null(failed);
            Assert.Equal(queries, cache.BoundaryQueries);
        }
        finally { fixture.MakeCurrent(); cache.InvalidateAll(); }
    }

    /// <summary>Capability registration readiness is checked at entry without storing a replacement token.</summary>
    [Fact]
    public void UnregisteredContextRejectsEntry()
    {
        var cache = Prepare();
        var declaration = new EngineBoundaryDeclaration("Registered", new PipelineStateCoverage(depth: DepthStateKnowledge.WriteEnabled));
        RenderContextRegistry.Retire(fixture);
        try
        {
            long queries = cache.BoundaryQueries;
            Assert.False(cache.TryBeginEngineBoundary(declaration, out var missing));
            Assert.Null(missing);
            Assert.Equal(queries, cache.BoundaryQueries);
        }
        finally { RenderContextRegistry.RegisterCurrent(fixture, static owner => ((HeadlessGLFixture)owner).IsContextValid); }
        Assert.True(cache.TryBeginEngineBoundary(declaration, out var scope));
        cache.ReleaseEngineBoundary(scope!);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Establishes a live fixture with unknown mutable state and an already cached output capability.</summary>
    private StateCache Prepare()
    {
        fixture.MakeCurrent();
        var cache = StateCache.Current;
        cache.InvalidateAll();
        _ = cache.MaxDrawBuffers;
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        return cache;
    }
    #endregion
}
