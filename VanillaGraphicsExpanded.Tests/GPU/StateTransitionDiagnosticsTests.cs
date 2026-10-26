using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
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
#if DEBUG
            long checks = cache.BoundaryErrorChecks, queries = cache.BoundaryQueries, calls = cache.FixedFunctionCalls;
#endif
            GL.Enable((EnableCap)(-1));
            cache.SetLineSmooth(true);
            cache.SetLineSmooth(true);
            cache.SetLineSmooth(false);
            cache.SetLineSmooth(false);
            cache.SetPointSpriteOrigin(PointSpriteCoordOriginParameter.LowerLeft);
            cache.SetPointSpriteOrigin(PointSpriteCoordOriginParameter.LowerLeft);
#if DEBUG
            Assert.Equal(3, cache.FixedFunctionCalls - calls);
            Assert.Equal(checks, cache.BoundaryErrorChecks);
            Assert.Equal(queries, cache.BoundaryQueries);
#endif
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
        using var pack = OpenGLPixelLayouts.PackScope(cache, new(1));
        using var unpack = OpenGLPixelLayouts.UnpackScope(cache, new(1));
#if DEBUG
        long checks = cache.BoundaryErrorChecks, queries = cache.BoundaryQueries;
#endif
        GL.Enable((EnableCap)(-1));
        OpenGLPixelLayouts.SetPack(cache, new(1));
        OpenGLPixelLayouts.SetUnpack(cache, new(1));
#if DEBUG
        Assert.Equal(checks, cache.BoundaryErrorChecks);
        Assert.Equal(queries, cache.BoundaryQueries);
#endif
        Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
    }

    /// <summary>Changed layouts and scope restoration follow the optional diagnostic policy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PixelLayoutChangesUseOptionalDiagnostics(bool enabled)
    {
        fixture.MakeCurrent();
        var cache = StateCache.Current;
        var pack = OpenGLPixelLayouts.GetPack(cache);
        var unpack = OpenGLPixelLayouts.GetUnpack(cache);
        bool diagnostics = GlDebug.CheckStateTransitions;
        GlDebug.CheckStateTransitions = enabled;
        try
        {
            Assert.Equal(ErrorCode.NoError, GL.GetError());
#if DEBUG
            long checks = cache.BoundaryErrorChecks, queries = cache.BoundaryQueries;
#endif
            // An unrelated pending error must survive normal changes and both scope disposals.
            if (!enabled) GL.Enable((EnableCap)(-1));
            using (OpenGLPixelLayouts.PackScope(cache, pack with { Alignment = pack.Alignment == 1 ? 4 : 1 }))
            using (OpenGLPixelLayouts.UnpackScope(cache, unpack with { Alignment = unpack.Alignment == 1 ? 4 : 1 }))
            {
                Assert.NotEqual(pack.Alignment, GL.GetInteger(GetPName.PackAlignment));
                Assert.NotEqual(unpack.Alignment, GL.GetInteger(GetPName.UnpackAlignment));
            }
#if DEBUG
            Assert.Equal(enabled ? 8 : 0, cache.BoundaryErrorChecks - checks);
            Assert.Equal(queries, cache.BoundaryQueries);
#endif
            Assert.Equal(enabled ? ErrorCode.NoError : ErrorCode.InvalidEnum, GL.GetError());
            Assert.Equal(pack.Alignment, GL.GetInteger(GetPName.PackAlignment));
            Assert.Equal(unpack.Alignment, GL.GetInteger(GetPName.UnpackAlignment));
        }
        finally { GlDebug.CheckStateTransitions = diagnostics; }
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
#if DEBUG
            long calls = cache.FixedFunctionCalls, checks = cache.BoundaryErrorChecks;
#endif
            cache.SetLineSmooth(false);
#if DEBUG
            Assert.Equal(calls + 1, cache.FixedFunctionCalls);
            Assert.Equal(checks + 2, cache.BoundaryErrorChecks);
#endif
            cache.SetLineSmooth(false);
#if DEBUG
            Assert.Equal(checks + 2, cache.BoundaryErrorChecks);
#endif
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
#if DEBUG
            long checks = cache.BoundaryErrorChecks, calls = cache.FixedFunctionCalls;
#endif
            boundary!.Dispose();
#if DEBUG
            Assert.Equal(2, cache.BoundaryErrorChecks - checks);
            Assert.Equal(1, cache.FixedFunctionCalls - calls);
#endif
            Assert.False(GL.IsEnabled(EnableCap.LineSmooth));
        }
        finally { GlDebug.CheckStateTransitions = diagnostics; }
    }
    #endregion
}
