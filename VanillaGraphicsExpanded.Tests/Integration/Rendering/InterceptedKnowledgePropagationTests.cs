using TestUniformRing = VanillaGraphicsExpanded.Tests.Support.GPU.Native.TestUniformRing;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks that observed mutations and routine lifecycle events preserve authoritative executed knowledge.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class InterceptedKnowledgePropagationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Integer and floating-point pixel-store effects reach known layouts without subsequent queries.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PixelStorePublishesKnownFieldsWithoutRecapture(bool floatingPoint)
    {
        // Arrange: Seed a complete pixel layout and track which field the external API call changes.
        // Act: Route an integer or floating-point pixel-store mutation through the interception boundary.
        // Assert: The changed fields become known without recapturing unrelated layout values.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        using var pack = OpenGLPixelLayouts.PackScope(cache, new(4));
        using var unpack = OpenGLPixelLayouts.UnpackScope(cache, new(4));
        if (floatingPoint)
        {
            GL.PixelStore(PixelStoreParameter.PackRowLength, 6.6f);
            GL.PixelStore(PixelStoreParameter.UnpackImageHeight, 12.6f);
            GL.PixelStore(PixelStoreParameter.PackSwapBytes, 0.25f);
        }
        else
        {
            GL.PixelStore(PixelStoreParameter.PackRowLength, 7);
            GL.PixelStore(PixelStoreParameter.UnpackImageHeight, 13);
            GL.PixelStore(PixelStoreParameter.PackSwapBytes, -2);
        }
        // Driver observations are outside the capture interval so the independent oracle
        // does not become indistinguishable from an unnecessary cache recovery query.
        Assert.Equal(7, GL.GetInteger(GetPName.PackRowLength));
        Assert.Equal(13, GL.GetInteger(GetPName.UnpackImageHeight));
        Assert.Equal(1, GL.GetInteger(GetPName.PackSwapBytes));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        using var calls = new BindingCallCapture();
        Assert.Equal(new OpenGlStateCache.PixelPackState(4, RowLength: 7, SwapBytes: true), OpenGLPixelLayouts.GetPack(cache));
        Assert.Equal(new OpenGlStateCache.PixelUnpackState(4, ImageHeight: 13), OpenGLPixelLayouts.GetUnpack(cache));
        Assert.Equal(0, calls.Calls(nameof(GL.GetInteger)));
    }

    /// <summary>A rejected pixel-store argument leaves the old certified field and native error unchanged.</summary>
    [Fact]
    public void RejectedPixelStorePreservesKnownLayoutAndError()
    {
        // Arrange: Establish a certified layout and a pending native error before the rejected call.
        // Act: Submit an invalid pixel-store value through the native entrypoint.
        // Assert: The certified field and native error remain unchanged.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        using var pack = OpenGLPixelLayouts.PackScope(cache, new(8, RowLength: 11));
        GL.PixelStore(PixelStoreParameter.PackAlignment, 3);
        Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
        Assert.Equal(8, GL.GetInteger(GetPName.PackAlignment));
        using var calls = new BindingCallCapture();
        Assert.Equal(new OpenGlStateCache.PixelPackState(8, RowLength: 11), OpenGLPixelLayouts.GetPack(cache));
        Assert.Equal(0, calls.Calls(nameof(GL.GetInteger)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Initial acquisition queries missing fields while retaining an independently observed row length.</summary>
    [Fact]
    public void PartialPixelKnowledgeQueriesOnlyMissingFields()
    {
        // Arrange: Preserve one independently observed row-length fact while leaving other layout fields unknown.
        // Act: Request layout state after partial knowledge has been published.
        // Assert: Only missing fields are queried and the observed row length is retained.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        using var pack = OpenGLPixelLayouts.PackScope(cache, new(8));
        cache.DirtyPixelPackState();
        GL.PixelStore(PixelStoreParameter.PackRowLength, 17);
        using var calls = new BindingCallCapture();
        Assert.Equal(new OpenGlStateCache.PixelPackState(8, RowLength: 17), OpenGLPixelLayouts.GetPack(cache));
        Assert.Equal(7, calls.Calls(nameof(GL.GetInteger)));
        calls.Reset();
        Assert.Equal(17, OpenGLPixelLayouts.GetPack(cache).RowLength);
        Assert.Equal(0, calls.Calls(nameof(GL.GetInteger)));
    }

    /// <summary>Exact external routing updates the same route used for later capture and managed suppression.</summary>
    [Fact]
    public void DrawRoutingPublishesWithoutReadbackAndSuppressesEqualManagedRequest()
    {
        // Arrange: Establish an external draw-buffer selection and observe the corresponding native route.
        // Act: Publish that route, then request the same route through the managed setter.
        // Assert: The managed repeat is suppressed without a native readback.
        EnsureContextValid();
        using var target = CreateMRTRenderTarget(2, 2, TextureFormat.Rgba32f, TextureFormat.Rgba32f);
        target.Bind();
        var cache = OpenGlStateCache.Current;
        GL.DrawBuffers(2, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment1]);
        Assert.Equal((int)DrawBuffersEnum.None, GL.GetInteger(GetPName.DrawBuffer0));
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment1, GL.GetInteger(GetPName.DrawBuffer1));
        using var work = new FramebufferRoutingWorkCapture();
        var actual = OpenGLFramebufferRouting.Capture(cache, target.FboId);
        Assert.Equal(DrawBuffersEnum.None, actual[0]);
        Assert.Equal(DrawBuffersEnum.ColorAttachment1, actual[1]);
        OpenGLFramebufferRouting.SetDrawBuffers(cache, 2, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment1]);
        Assert.Equal(0, work.Captures);
        Assert.Equal(0, work.DynamicApplications);
        // Calibrate the oracle through external dispatch, then force a managed saved-original write.
        GL.DrawBuffers(2, [DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.None]);
        Assert.Equal(1, work.DynamicApplications);
        OpenGLFramebufferRouting.SetDrawBuffers(cache, 2, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment1]);
        Assert.Equal(2, work.DynamicApplications);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Window routing uses captured surface capabilities instead of recapturing known draw-buffer state.</summary>
    [Fact]
    public void WindowRoutingPublishesGroupedAndPhysicalSelectorsWithoutRecapture()
    {
        // Arrange: Capture window surface capabilities and seed known grouped and physical draw selectors.
        // Act: Apply window routing and read the resulting selector facts through the observer.
        // Assert: Grouped and physical routes become known without redundant capture.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        using var bindings = cache.BindFramebufferScope(FramebufferTarget.DrawFramebuffer, 0);
        int previous = GL.GetInteger(GetPName.DrawBuffer0);
        DrawBufferMode grouped = GpuSupport.Graphics.DoubleBuffered == true ? DrawBufferMode.Back : DrawBufferMode.Front;
        try
        {
            GL.DrawBuffer(grouped);
            Assert.Equal((int)grouped, GL.GetInteger(GetPName.DrawBuffer0));
            using (var capture = new FramebufferRoutingWorkCapture())
            {
                Assert.Equal((DrawBuffersEnum)grouped, OpenGLFramebufferRouting.Capture(cache, 0)[0]);
                Assert.Equal(0, capture.Captures);
            }
            GL.DrawBuffers(1, [DrawBuffersEnum.FrontLeft]);
            Assert.Equal((int)DrawBuffersEnum.FrontLeft, GL.GetInteger(GetPName.DrawBuffer0));
            using (var capture = new FramebufferRoutingWorkCapture())
            {
                Assert.Equal(DrawBuffersEnum.FrontLeft, OpenGLFramebufferRouting.Capture(cache, 0)[0]);
                Assert.Equal(0, capture.Captures);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DrawBuffer((DrawBufferMode)previous); }
    }

    /// <summary>Named routing rejects nonexistent identities without certifying them or discarding unrelated routes.</summary>
    [Fact]
    public void RejectedNamedRoutingPreservesUnrelatedKnowledge()
    {
        // Arrange: Publish valid named routes before submitting a nonexistent selector identity.
        // Act: Attempt to publish the unsupported named route.
        // Assert: Rejection preserves unrelated route knowledge and does not certify the missing identity.
        EnsureContextValid();
        using var target = CreateRenderTarget(2, 2, TextureFormat.Rgba32f);
        var cache = OpenGlStateCache.Current;
        Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out var expected));
        GL.NamedFramebufferDrawBuffers(int.MaxValue, 1, [DrawBuffersEnum.ColorAttachment0]);
        Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
        Assert.False(cache.TryCopyFramebufferDrawRouting(int.MaxValue, out _));
        Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out var actual));
        Assert.Equal(expected, actual);
    }

    /// <summary>Neither caller arrays nor returned snapshots can mutate a completed external route publication.</summary>
    [Fact]
    public void ExternalRoutingCopiesCallerAndCaptureArrays()
    {
        // Arrange: Retain caller-owned routing arrays and a later capture array for mutation checks.
        // Act: Publish the route, then mutate both source and returned arrays.
        // Assert: Published routing remains isolated from those caller mutations.
        EnsureContextValid();
        using var target = CreateMRTRenderTarget(2, 2, TextureFormat.Rgba32f, TextureFormat.Rgba32f);
        target.Bind();
        var cache = OpenGlStateCache.Current;
        DrawBuffersEnum[] route = [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment1];
        GL.DrawBuffers(route.Length, route);
        route[1] = DrawBuffersEnum.ColorAttachment0;
        var snapshot = OpenGLFramebufferRouting.Capture(cache, target.FboId);
        Assert.Equal(DrawBuffersEnum.ColorAttachment1, snapshot[1]);
        snapshot[1] = DrawBuffersEnum.None;
        Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out var actual));
        Assert.Equal(DrawBuffersEnum.ColorAttachment1, actual[1]);
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment1, GL.GetInteger(GetPName.DrawBuffer1));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Losing framebuffer selectors does not discard established per-object routing facts.</summary>
    [Fact]
    public void SelectorLossPreservesFramebufferLocalRouting()
    {
        // Arrange: Publish per-framebuffer routing facts before losing global selector knowledge.
        // Act: Invalidate selector state without changing the framebuffer-local observations.
        // Assert: Established per-object routes remain available after selector loss.
        EnsureContextValid();
        using var target = CreateRenderTarget(2, 2, TextureFormat.Rgba32f);
        target.Bind();
        var cache = OpenGlStateCache.Current;
        Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out var expected));
        cache.Invalidate(EPipelineState.FramebufferBindings);
        Assert.False(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out _));
        Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(target.FboId, cache.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer));
    }

    /// <summary>External depth-mask changes are exact calls, while an equal managed transition is suppressed.</summary>
    [Fact]
    public void FixedDepthMaskPublishesAndSuppressesManagedRepeat()
    {
        // Arrange: Seed depth-mask state and track the intercepted native setter call.
        // Act: Change the mask externally, then request the same value through managed state.
        // Assert: External state is published and the equal managed transition is suppressed.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        cache.SetDepthWriteMask(true);
        GL.DepthMask(false);
        Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
        using var calls = new BindingCallCapture();
        cache.SetDepthWriteMask(false);
        Assert.Equal(0, calls.Calls(nameof(GL.DepthMask)));
        Assert.Equal(0, calls.Calls(nameof(GL.GetInteger)));
        cache.SetDepthWriteMask(true);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A rejected fixed-state selector preserves both the native error and previously certified suppression.</summary>
    [Fact]
    public void RejectedDepthFunctionPreservesPriorKnowledge()
    {
        // Arrange: Establish a valid depth function and native error before submitting an unsupported enum.
        // Act: Submit the rejected depth-function selector.
        // Assert: The prior certified function and native error remain intact.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        cache.SetDepthFunc(DepthFunction.Less);
        GL.DepthFunc((DepthFunction)(-1));
        Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
        Assert.Equal((int)DepthFunction.Less, GL.GetInteger(GetPName.DepthFunc));
        using var calls = new BindingCallCapture();
        cache.SetDepthFunc(DepthFunction.Less);
        Assert.Equal(0, calls.Calls(nameof(GL.DepthFunc)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Known generic buffer assignments propagate without querying the binding selector afterward.</summary>
    [Fact]
    public void EstablishedBufferBindingPublishesWithoutRecapture()
    {
        // Arrange: Bind a known buffer through the observed native command and clear query counters.
        // Act: Observe the external binding and then read its managed publication.
        // Assert: The known buffer binding propagates without querying the native selector again.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
        using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
        { Role = BufferRole.Vertex, Capacity = 32, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw }).RequireResource();
        var cache = OpenGlStateCache.Current;
        using var scope = cache.BindBufferScope(BufferTarget.ArrayBuffer, 0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, buffer.BufferId);
        Assert.Equal(buffer.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
        using var calls = new BindingCallCapture();
        Assert.True(cache.TryGetCachedBoundBuffer(BufferTarget.ArrayBuffer, out int selected));
        Assert.Equal(buffer.BufferId, selected);
        Assert.Equal(buffer.BufferId, cache.GetBoundBuffer(BufferTarget.ArrayBuffer));
        Assert.Equal(0, calls.Calls(nameof(GL.GetInteger)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Indexed ranges publish both object-local range facts and their generic binding alias.</summary>
    [Theory]
    [InlineData(64)]
    [InlineData(256)]
    public void IndexedBufferRangePublishesGenericAliasWithoutRecapture(int selectedSize)
    {
        // Arrange: Bind an indexed buffer range with a selected extent and monitor both range and generic aliases.
        // Act: Publish the indexed range through the external binding boundary.
        // Assert: Both object-local range facts and the generic alias identify the same buffer.
        EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
        using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription
        { Role = BufferRole.ShaderStorage, Capacity = 128, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.DynamicDraw }).RequireResource();
        var cache = OpenGlStateCache.Current;
        using var scope = cache.BindBufferScope(BufferTarget.ShaderStorageBuffer, 0);
        try
        {
            GL.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 0, buffer.BufferId, 0, selectedSize);
            Assert.Equal(buffer.BufferId, GL.GetInteger(GetPName.ShaderStorageBufferBinding));
            using var calls = new BindingCallCapture();
            Assert.True(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.ShaderStorageBuffer, 0,
                out int selected, out nint offset, out nint size));
            Assert.Equal(buffer.BufferId, selected);
            Assert.Equal((nint)0, offset);
            Assert.Equal((nint)selectedSize, size);
            Assert.Equal(buffer.BufferId, cache.GetBoundBuffer(BufferTarget.ShaderStorageBuffer));
            Assert.Equal(0, calls.Calls(nameof(GL.GetInteger)));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, 0); }
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Uniform frame advancement and screen resize keep known selectors, routing and pixel layout intact.</summary>
    [Fact]
    public void FrameAndResizePreserveKnowledgeWhileUniformEpochAdvances()
    {
        // Arrange: Seed known selectors, framebuffer routes, and pixel layouts before advancing the frame epoch.
        // Act: Advance frame/resize lifecycle and update the uniform publication.
        // Assert: Unrelated known state persists while the uniform epoch changes.
        EnsureContextValid();
        using var target = CreateRenderTarget(2, 2, TextureFormat.Rgba32f);
        target.Bind();
        var cache = OpenGlStateCache.Current;
        using var pack = OpenGLPixelLayouts.PackScope(cache, new(8, RowLength: 17));
        using var ring = new GpuUniformRingBuffer(4096, 2, preferPersistent: false, coherent: false, debugName: "Tests.KnowledgeEpoch");
        using var controller = new RHIUniformRingFrameController(ring);
        var renderer = new RHIUniformRingBeginRenderer(controller);
        try
        {
            renderer.OnRenderFrame(0, EnumRenderStage.Before);
            var first = ring.AllocateAndWrite(new byte[16]);
            Assert.True(ring.CanReuse(first));
            controller.EndFrame();
            renderer.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.False(ring.CanReuse(first));
            ScreenResourceManager.HandleScreenResize();
            using var calls = new BindingCallCapture();
            Assert.Equal(target.FboId, cache.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer));
            Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out _));
            Assert.Equal(new OpenGlStateCache.PixelPackState(8, RowLength: 17), OpenGLPixelLayouts.GetPack(cache));
            Assert.Equal(0, calls.Calls(nameof(GL.GetInteger)));
            controller.EndFrame();
        }
        finally { controller.Dispose(); TestUniformRing.EnsureFrame(); }
    }
    #endregion
}
