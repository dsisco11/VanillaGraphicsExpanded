using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies normalized mutable routes, explicit invalidation and native-operation suppression.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class FramebufferRoutingCacheTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    #region Normalized routes
    /// <summary>Per-target mappings ignore trailing None and cannot alias caller arrays or captured snapshots.</summary>
    [Fact]
    public void SparseRoutesAreCopiedAndNormalizedPerFramebuffer()
    {
        EnsureContextValid();
        using var first = Target();
        using var second = Target();
        var cache = StateCache.Current;
        DrawBuffersEnum[] authored = [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment2];
        using (var capture = new GraphicsTransitionCapture())
        {
            first.Bind();
            cache.SetDrawBuffers(2, authored);
            authored[1] = DrawBuffersEnum.ColorAttachment0;
            var snapshot = cache.CaptureFramebufferDrawRouting(first.FboId);
            Assert.Equal(DrawBuffersEnum.ColorAttachment2, snapshot[1]);
            snapshot[1] = DrawBuffersEnum.None;
            cache.SetDrawBuffers(4, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.None, DrawBuffersEnum.None]);
            second.Bind();
            cache.SetDrawBuffers(2, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment2]);
            first.Bind();
            cache.SetDrawBuffers(2, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment2]);
            Assert.Equal(2, capture.Calls.Count(call => call.StartsWith("Routing")));
            Assert.Equal((int)DrawBuffersEnum.ColorAttachment2, GL.GetInteger(GetPName.DrawBuffer1));
            Assert.Equal(DrawBuffersEnum.ColorAttachment2, cache.CaptureFramebufferDrawRouting(first.FboId)[1]);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Scalar and array setters share complete mappings, including disabled unspecified outputs.</summary>
    [Fact]
    public void ScalarAndArraySettersShareTheSameMapping()
    {
        EnsureContextValid();
        using var target = Target();
        target.Bind();
        var cache = StateCache.Current;
        using var capture = new GraphicsTransitionCapture();
        cache.SetDrawBuffer(DrawBufferMode.ColorAttachment1);
        cache.SetDrawBuffers(1, [DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2]);
        Assert.Single(capture.Calls, call => call.StartsWith("Routing"));
        Assert.Equal((int)DrawBuffersEnum.None, GL.GetInteger(GetPName.DrawBuffer1));
        cache.SetDrawBuffers(2, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment2]);
        Assert.Equal(2, capture.Calls.Count(call => call.StartsWith("Routing")));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Identical restoration returns before named calls or fallback target binds, even when another target is selected.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdenticalRestorationSkipsAllNativeWork(bool directStateAccess)
    {
        EnsureContextValid();
        using var capability = new DirectStateAccessCapabilityScope(directStateAccess);
        using var first = Target();
        using var second = Target();
        var cache = StateCache.Current;
        first.Bind();
        cache.SetDrawBuffer(DrawBufferMode.ColorAttachment1);
        second.Bind();
        using var capture = new GraphicsTransitionCapture();
        cache.RestoreFramebufferDrawRouting(first.FboId, [DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.None]);
        Assert.Empty(capture.Calls);
        Assert.Equal(second.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region Invalidated and retired identities
    /// <summary>Explicit routing, legacy binding and full context-state invalidations all reject stale native route assumptions.</summary>
    [Theory]
    [InlineData(EPipelineState.FramebufferRouting)]
    [InlineData(EPipelineState.FramebufferBindings)]
    [InlineData(EPipelineState.All)]
    public void ExternalMutationRequiresFreshPublication(EPipelineState invalidation)
    {
        EnsureContextValid();
        using var target = Target();
        target.Bind();
        var cache = StateCache.Current;
        cache.SetDrawBuffer(DrawBufferMode.ColorAttachment1);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        cache.Invalidate(invalidation);
        using var capture = new GraphicsTransitionCapture();
        cache.SetDrawBuffer(DrawBufferMode.ColorAttachment1);
        Assert.Single(capture.Calls, call => call.StartsWith("Routing"));
        Assert.Equal((int)DrawBufferMode.ColorAttachment1, GL.GetInteger(GetPName.DrawBuffer0));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>A failed native call cannot publish its attempted mapping or retain stale knowledge for that target.</summary>
    [Fact]
    public void FailedNativePublicationInvalidatesOnlyAffectedRoute()
    {
        EnsureContextValid();
        using var first = Target();
        using var second = Target();
        var cache = StateCache.Current;
        second.Bind();
        cache.SetDrawBuffer(DrawBufferMode.ColorAttachment1);
        first.Bind();
        cache.SetDrawBuffer(DrawBufferMode.ColorAttachment0);
        using var capture = new GraphicsTransitionCapture();
        Assert.Throws<InvalidOperationException>(() => cache.SetDrawBuffers(1, [(DrawBuffersEnum)DrawBufferMode.Back]));
        cache.SetDrawBuffer(DrawBufferMode.ColorAttachment0);
        Assert.Equal(2, capture.Calls.Count(call => call.StartsWith("Routing")));
        second.Bind();
        cache.SetDrawBuffer(DrawBufferMode.ColorAttachment1);
        Assert.Equal(2, capture.Calls.Count(call => call.StartsWith("Routing")));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Transferred native identities and same-name external refresh cannot retain routes authored by their previous owner.</summary>
    [Fact]
    public void DetachAndWrappedRefreshForgetSameNativeIdentity()
    {
        EnsureContextValid();
        using var target = Target();
        target.Bind();
        var cache = StateCache.Current;
        cache.SetDrawBuffer(DrawBufferMode.ColorAttachment1);
        int id = (int)target.Detach();
        try
        {
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            Assert.Equal(DrawBuffersEnum.ColorAttachment0, cache.CaptureFramebufferDrawRouting(id)[0]);
            using var wrapped = DynamicGpuFramebuffer.Wrap(id, width: 2, height: 2);
            cache.SetDrawBuffer(DrawBufferMode.ColorAttachment1);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            wrapped.RefreshWrappedFramebuffer(id, 2, 2);
            Assert.Equal(DrawBuffersEnum.ColorAttachment0, cache.CaptureFramebufferDrawRouting(id)[0]);
        }
        finally { cache.DeleteFramebuffer(id); }
        using var replacement = Target();
        replacement.Bind();
        Assert.Equal(DrawBuffersEnum.ColorAttachment0, cache.CaptureFramebufferDrawRouting(replacement.FboId)[0]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region Repeated submission
    /// <summary>Repeated equal routes suppress native operations while changed routes and final restoration preserve identical clears.</summary>
    [Fact]
    public void CachedPassesMatchExplicitlyForgottenRoutesWithFewerCalls()
    {
        EnsureContextValid();
        // Clear-only passes declare no executable and restore the incoming shared-fixture program.
        using var program = StateCache.Current.UseProgramScope(0);
        using var target = Target();
        var cache = StateCache.Current;
        // Reset image contents and the incoming mapping identically for each comparison.
        void Initialize()
        {
            foreach (var image in new[] { target[0], target[1], target[2] }) image.UploadDataImmediate(new float[16]);
            target.Bind();
            cache.SetDrawBuffer(DrawBufferMode.ColorAttachment0);
        }
        // Explicit invalidation models an owner without remembered live routing.
        List<string> Run(bool forget)
        {
            using var capture = new GraphicsTransitionCapture();
            Assert.True(GraphicsCommandContext.TryRun("Tests.RoutingCache", [], true, commands =>
                        {
                            for (int step = 0; step < 4; step++)
                            {
                                if (forget) cache.Invalidate(EPipelineState.FramebufferRouting);
                                commands.BeginPass(new(target, [new(step == 3 ? 1 : 0, AttachmentLoad.Clear, Clear: ColorClearValue.Float(step == 3 ? .5f : (step + 1) * .25f, 0, 0, 1))]));
                                commands.EndPass();
                            }
                        }));
            return [.. capture.Calls];
        }
        Initialize();
        var uncached = Run(true);
        var expected = new[] { target[0].ReadPixels(), target[1].ReadPixels() };
        Initialize();
        var cached = Run(false);
        var actual = new[] { target[0].ReadPixels(), target[1].ReadPixels() };
        Assert.Equal(expected[0], actual[0]);
        Assert.Equal(expected[1], actual[1]);
        Assert.Equal(.75f, actual[0][0]);
        Assert.Equal(.5f, actual[1][0]);
        Assert.True(cached.Count(call => call.StartsWith("Routing")) < uncached.Count(call => call.StartsWith("Routing")));
        output.WriteLine("DYNAMIC_ROUTING_CACHE=" + System.Text.Json.JsonSerializer.Serialize(new { uncached, cached }));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Creates a three-output dynamic target so sparse route changes remain valid native configurations.</summary>
    private DynamicGpuFramebuffer Target() => (DynamicGpuFramebuffer)CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
    #endregion
}
