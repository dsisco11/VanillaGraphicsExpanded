using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Separates ordinary transition costs from explicitly checked native operations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class StateTransitionDiagnosticsTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Normal changes and repeats preserve pending driver errors without issuing error queries.</summary>
    [Fact]
    public void OrdinaryRasterChangesDoNotPollErrors()
    {
        fixture.MakeCurrent();
        var cache = StateCache.Current;
        cache.InvalidateAll();
        bool diagnostics = GlDebug.CheckStateTransitions;
        GlDebug.CheckStateTransitions = false;
        try
        {
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            long checks = cache.BoundaryErrorChecks, queries = cache.BoundaryQueries, calls = cache.FixedFunctionCalls;
            GL.Enable((EnableCap)(-1));
            cache.SetLineSmooth(true);
            cache.SetLineSmooth(true);
            cache.SetLineSmooth(false);
            cache.SetLineSmooth(false);
            cache.SetPointSpriteOrigin(PointSpriteCoordOriginParameter.LowerLeft);
            cache.SetPointSpriteOrigin(PointSpriteCoordOriginParameter.LowerLeft);
            Assert.Equal(3, cache.FixedFunctionCalls - calls);
            Assert.Equal(checks, cache.BoundaryErrorChecks);
            Assert.Equal(queries, cache.BoundaryQueries);
            Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
            Assert.False(GL.IsEnabled(EnableCap.LineSmooth));
        }
        finally { GlDebug.CheckStateTransitions = diagnostics; }
    }

    /// <summary>Identical pixel layouts avoid queries and preserve errors for their actual owner.</summary>
    [Fact]
    public void IdenticalPixelLayoutsDoNotConsumeErrors()
    {
        fixture.MakeCurrent();
        var cache = StateCache.Current;
        using var pack = cache.SetPixelPackScope(new(1));
        using var unpack = cache.SetPixelUnpackScope(new(1));
        long checks = cache.BoundaryErrorChecks, queries = cache.BoundaryQueries;
        GL.Enable((EnableCap)(-1));
        cache.SetPixelPackState(new(1));
        cache.SetPixelUnpackState(new(1));
        Assert.Equal(checks, cache.BoundaryErrorChecks);
        Assert.Equal(queries, cache.BoundaryQueries);
        Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
    }

    /// <summary>Enabled diagnostics reject inherited errors and retry the field instead of trusting failed publication.</summary>
    [Fact]
    public void DiagnosticsRejectErrorAndRetryUnknownField()
    {
        fixture.MakeCurrent();
        var cache = StateCache.Current;
        bool diagnostics = GlDebug.CheckStateTransitions;
        GlDebug.CheckStateTransitions = true;
        try
        {
            cache.SetLineSmooth(false);
            GL.Enable((EnableCap)(-1));
            Assert.Throws<InvalidOperationException>(() => cache.SetLineSmooth(true));
            long calls = cache.FixedFunctionCalls, checks = cache.BoundaryErrorChecks;
            cache.SetLineSmooth(false);
            Assert.Equal(calls + 1, cache.FixedFunctionCalls);
            Assert.Equal(checks + 2, cache.BoundaryErrorChecks);
            cache.SetLineSmooth(false);
            Assert.Equal(checks + 2, cache.BoundaryErrorChecks);
            Assert.False(GL.IsEnabled(EnableCap.LineSmooth));
        }
        finally { GlDebug.CheckStateTransitions = diagnostics; }
    }

    /// <summary>Restoration owns one check pair even when per-command diagnostics are enabled.</summary>
    [Fact]
    public void RestorationSuppressesNestedDiagnostics()
    {
        fixture.MakeCurrent();
        var cache = StateCache.Current;
        bool diagnostics = GlDebug.CheckStateTransitions;
        GlDebug.CheckStateTransitions = true;
        try
        {
            cache.SetLineSmooth(false);
            Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("RasterDiagnostics",
                new PipelineStateCoverage(rasterizer: RasterizerStateKnowledge.LineSmooth)), out var boundary));
            cache.SetLineSmooth(true);
            long checks = cache.BoundaryErrorChecks, calls = cache.FixedFunctionCalls;
            boundary!.Dispose();
            Assert.Equal(2, cache.BoundaryErrorChecks - checks);
            Assert.Equal(1, cache.FixedFunctionCalls - calls);
            Assert.False(GL.IsEnabled(EnableCap.LineSmooth));
        }
        finally { GlDebug.CheckStateTransitions = diagnostics; }
    }
    #endregion
}
