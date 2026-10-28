using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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
        var cache = OpenGlStateCache.Current;
        DrawBuffersEnum[] authored = [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment2];
        using (var capture = new GraphicsTransitionCapture())
        {
            first.Bind();
            OpenGLFramebufferRouting.SetDrawBuffers(cache, 2, authored);
            authored[1] = DrawBuffersEnum.ColorAttachment0;
            var snapshot = OpenGLFramebufferRouting.Capture(cache, first.FboId);
            Assert.Equal(DrawBuffersEnum.ColorAttachment2, snapshot[1]);
            snapshot[1] = DrawBuffersEnum.None;
            OpenGLFramebufferRouting.SetDrawBuffers(cache, 4, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.None, DrawBuffersEnum.None]);
            second.Bind();
            OpenGLFramebufferRouting.SetDrawBuffers(cache, 2, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment2]);
            first.Bind();
            OpenGLFramebufferRouting.SetDrawBuffers(cache, 2, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment2]);
            Assert.Equal(2, capture.Calls.Count(call => call.StartsWith("Routing")));
            Assert.Equal((int)DrawBuffersEnum.ColorAttachment2, GL.GetInteger(GetPName.DrawBuffer1));
            Assert.Equal(DrawBuffersEnum.ColorAttachment2, OpenGLFramebufferRouting.Capture(cache, first.FboId)[1]);
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
        var cache = OpenGlStateCache.Current;
        using var capture = new GraphicsTransitionCapture();
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment1);
        OpenGLFramebufferRouting.SetDrawBuffers(cache, 1, [DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2]);
        Assert.Single(capture.Calls, call => call.StartsWith("Routing"));
        Assert.Equal((int)DrawBuffersEnum.None, GL.GetInteger(GetPName.DrawBuffer1));
        OpenGLFramebufferRouting.SetDrawBuffers(cache, 2, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment2]);
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
        var cache = OpenGlStateCache.Current;
        first.Bind();
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment1);
        second.Bind();
        using var capture = new GraphicsTransitionCapture();
        OpenGLFramebufferRouting.Restore(cache, first.FboId, [DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.None]);
        Assert.Empty(capture.Calls);
        Assert.Equal(second.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region Invalidated and retired identities
    /// <summary>Explicit routing uncertainty and full context-state loss reject stale native route assumptions.</summary>
    [Theory]
    [InlineData(EPipelineState.FramebufferRouting)]
    [InlineData(EPipelineState.All)]
    public void ExternalMutationRequiresFreshPublication(EPipelineState invalidation)
    {
        EnsureContextValid();
        using var target = Target();
        target.Bind();
        var cache = OpenGlStateCache.Current;
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment1);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        cache.Invalidate(invalidation);
        using var capture = new GraphicsTransitionCapture();
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment1);
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
        var cache = OpenGlStateCache.Current;
        second.Bind();
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment1);
        first.Bind();
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment0);
        using var capture = new GraphicsTransitionCapture();
        Assert.Throws<InvalidOperationException>(() => OpenGLFramebufferRouting.SetDrawBuffers(cache, 1, [(DrawBuffersEnum)DrawBufferMode.Back]));
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment0);
        Assert.Equal(2, capture.Calls.Count(call => call.StartsWith("Routing")));
        second.Bind();
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment1);
        Assert.Equal(2, capture.Calls.Count(call => call.StartsWith("Routing")));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Ownership transfer and same-name publication preserve routes; observed native changes update their knowledge.</summary>
    [Fact]
    public void DetachAndWrappedRefreshPreserveObservedNativeIdentity()
    {
        EnsureContextValid();
        using var target = Target();
        target.Bind();
        var cache = OpenGlStateCache.Current;
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment1);
        int id = (int)target.Detach();
        try
        {
            Assert.Equal(DrawBuffersEnum.ColorAttachment1, OpenGLFramebufferRouting.Capture(cache, id)[0]);
            EngineStateCalls.DrawBuffer(DrawBufferMode.ColorAttachment0);
            Assert.Equal(DrawBuffersEnum.ColorAttachment0, OpenGLFramebufferRouting.Capture(cache, id)[0]);
            using var wrapped = DynamicGpuFramebuffer.Wrap(id, width: 2, height: 2);
            OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment1);
            wrapped.RefreshWrappedFramebuffer(id, 2, 2);
            Assert.Equal(DrawBuffersEnum.ColorAttachment1, OpenGLFramebufferRouting.Capture(cache, id)[0]);
        }
        finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, id)); }
        using var replacement = Target();
        replacement.Bind();
        Assert.Equal(DrawBuffersEnum.ColorAttachment0, OpenGLFramebufferRouting.Capture(cache, replacement.FboId)[0]);
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
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
        using var target = Target();
        var cache = OpenGlStateCache.Current;
        // Reset image contents and the incoming mapping identically for each comparison.
        void Initialize()
        {
            foreach (var image in new[] { target[0], target[1], target[2] }) image.UploadDataImmediate(new float[16]);
            target.Bind();
            OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment0);
        }
        // Explicit invalidation models an owner without remembered live routing.
        List<string> Run(bool forget)
        {
            using var capture = new GraphicsTransitionCapture();
            {
    var commands=RHICommandContext.Current;
    using var scope43=commands.EnterBoundary(new("Tests.RoutingCache",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope43.Run(()=>{
                            for (int step = 0; step < 4; step++)
                            {
                                if (forget) cache.Invalidate(EPipelineState.FramebufferRouting);
                                commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:step == 3 ? 1 : 0, Load:AttachmentLoad.Clear, Clear: ColorClearValue.Float(step == 3 ? .5f : (step + 1) * .25f, 0, 0, 1))]));
                                commands.EndRenderPass();
                            }
                        });
}
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
    private DynamicGpuFramebuffer Target() => (DynamicGpuFramebuffer)CreateMRTRenderTarget(2, 2, TextureFormat.Rgba32f, TextureFormat.Rgba32f, TextureFormat.Rgba32f);
    #endregion
}
