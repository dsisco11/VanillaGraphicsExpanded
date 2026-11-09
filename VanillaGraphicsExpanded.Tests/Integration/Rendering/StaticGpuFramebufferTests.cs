using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Qualifies immutable framebuffer configuration, construction proof and repeated-pass fast paths.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class StaticGpuFramebufferTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{


    #region Public API
    #region Configuration and lifetime

    /// <summary>The declaration stays immutable while its owner preserves borrowed attachments until retirement.</summary>
    [Fact]
    public void ImmutableDeclarationBorrowsOwnerScheduledAttachments()
    {
        // Arrange: Create owner-scheduled attachments and declare an immutable view over their storage.
        // Act: Realize, use, and retire the static framebuffer declaration.
        // Assert: The declaration stays immutable while borrowed attachments live until owner retirement.
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var attachment = GpuFramebufferAttachment.FromTexture(texture);
        using var target = StaticGpuFramebuffer.Create([attachment]);
        using var alias = DynamicGpuFramebuffer.Wrap(target.FboId, width: 2, height: 2);
        Assert.Throws<InvalidOperationException>(() => target.Resize(3, 3));
        Assert.Throws<InvalidOperationException>(() => alias.SetAttachment(FramebufferSlot.Color0, attachment));
        Assert.Throws<InvalidOperationException>(() => alias.RemoveAttachment(FramebufferSlot.Color0));
        Assert.Throws<InvalidOperationException>(() => alias.Resize(3, 3));
        Assert.Throws<InvalidOperationException>(() => VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, target.FboId)));
        target.Bind();
        Assert.Throws<InvalidOperationException>(() => OpenGLFramebufferRouting.SetDrawBuffer(OpenGlStateCache.Current, DrawBufferMode.None));
        texture.UploadDataImmediate(Enumerable.Repeat(.375f, 16).ToArray());
        ImageAssertions.Comparisons.CompareConstant(texture, .375f).AssertEqual();
        target.Dispose();
        Assert.True(texture.Resize(3, 3));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>The owner keeps both the view and source allocation until static configuration retirement.</summary>
    [Fact]
    public void StaticArrayViewOwnerPreservesSourceUntilDetach()
    {
        // Arrange: Build an array view whose source allocation is owned by a separate producer.
        // Act: Use the static view and detach it from its declaration owner.
        // Assert: The source remains live until the static configuration retires.
        EnsureContextValid();
        using var array = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 3), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = array, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 3, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(view, layer: 1);
        using var target = StaticGpuFramebuffer.Create([image]);
        int id = (int)target.Detach(); try
        {
            Assert.True(array.Resize(3, 3, 3));
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, id));
        }
        finally { if (GL.IsFramebuffer(id)) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, id)); }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Construction rejects untracked native images and releases guards retained before that rejection.</summary>
    [Fact]
    public void RejectedUnmanagedAttachmentsDoNotLeakFrozenReferences()
    {
        // Arrange: Supply native images without tracked ownership metadata.
        // Act: Attempt immutable declaration construction after its guards are acquired.
        // Assert: Rejection releases every retained reference.
        EnsureContextValid();
        using var first = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var second = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var managed = GpuFramebufferAttachment.FromTexture(first);
        using var external = GpuFramebufferAttachment.FromTextureId(second.TextureId);
        Assert.Throws<ArgumentException>(() => StaticGpuFramebuffer.Create([managed, external]));
        Assert.True(first.Resize(3, 3));
        managed.Dispose();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region External native effects
    /// <summary>Raw native routing executes despite managed immutability and supersedes its former executed proof.</summary>
    [Fact]
    public void ExternalRoutingSupersedesImmutableDeclarationProof()
    {
        // Arrange: Realize a static route and retain its executed-state proof.
        // Act: Change routing through a raw native command.
        // Assert: The command executes and supersedes the former immutable proof.
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription {
            Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = 2, Height = 2,
            MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(texture);
        using var target = StaticGpuFramebuffer.Create([image]);
        target.Bind();
        var cache = OpenGlStateCache.Current;
        GL.NamedFramebufferDrawBuffer(target.FboId, DrawBufferMode.None);
        Assert.Equal((int)DrawBufferMode.None, GL.GetInteger(GetPName.DrawBuffer0));
        Assert.True(cache.TryCopyFramebufferDrawRouting(target.FboId, out var observed));
        Assert.Equal(DrawBuffersEnum.None, observed[0]);
        // Managed requests retain the declaration restriction but restore it when actual state differs.
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment0);
        Assert.Equal((int)DrawBufferMode.ColorAttachment0, GL.GetInteger(GetPName.DrawBuffer0));
        Assert.Throws<InvalidOperationException>(() => OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.None));
        GL.NamedFramebufferDrawBuffer(target.FboId, (DrawBufferMode)(-1));
        Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
        using var capture = new GraphicsTransitionCapture();
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment0);
        Assert.Empty(capture.Calls);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Retirement
    /// <summary>Worker retirement withdraws the originating cache declaration before queued native deletion.</summary>
    [Fact]
    public void WorkerDisposalReleasesDeclarationAndBackingAllocation()
    {
        // Arrange: Retain a static declaration and its backing owner before worker-thread disposal.
        // Act: Queue retirement and drain it through the owning context.
        // Assert: Declaration and backing allocation retire together.
        EnsureContextValid();
        // Other owning-image fixtures may retain their shared disposal service.
        // This case installs its own manager to exercise its actual queue drain.
        fixture.RetireResourceServices();
        using var manager = new RHIResourceManager();
        RHIResourceManagerSystem.Initialize(manager);
        try
        {
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            using var target = StaticGpuFramebuffer.Create([image]);
            int id = target.FboId;
            Exception? failure = null;
            var worker = new System.Threading.Thread(() =>
            {
                try { target.Dispose(); }
                catch (Exception error) { failure = error; }
            });
            worker.Start();
            worker.Join();
            Assert.Null(failure);
            Assert.False(target.IsDisposed);
            Assert.Throws<InvalidOperationException>(() => OpenGlStateCache.Current.RequireMutableFramebufferConfiguration(id));
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            OpenGlStateCache.Current.RequireMutableFramebufferConfiguration(id);
            Assert.True(texture.Resize(3, 3));
            Assert.False(GL.IsFramebuffer(id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RHIResourceManagerSystem.Shutdown();
            TextureStreamingSystem.Dispose();
        }
    }

    /// <summary>A retired context cannot keep immutable authority over an identical name in a new registration.</summary>
    [Fact]
    public void ContextRetirementWithdrawsStaticNativeIdentity()
    {
        // Arrange: Publish immutable authority for one native name in the current context.
        // Act: Retire that context and register a new context with the same numeric name.
        // Assert: Old static authority cannot apply to the new native identity.
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(texture);
        using var target = StaticGpuFramebuffer.Create([image]);
        int id = target.FboId;
        object replacement = new();
        try
        {
            // Keep the actual GL namespace alive while changing its managed generation.
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(fixture);
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
            OpenGlStateCache.Current.RequireMutableFramebufferConfiguration(id);
            target.Dispose();
            Assert.True(GL.IsFramebuffer(id));
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, id));
            Assert.False(GL.IsFramebuffer(id));
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(replacement);
            fixture.RestoreContextRegistration();
            if (GL.IsFramebuffer(id)) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, id));
        }
    }
    #endregion
    #region Immutable submission
    /// <summary>Reusable static declarations bypass all live route comparisons, snapshots, native writes and completeness checks.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticPassesHaveNoLiveRoutingWork(bool standalone)
    {
        // Arrange: Prepare a reusable static pass with complete immutable routing declarations.
        // Act: Submit it repeatedly, with or without standalone ownership.
        // Assert: Reuse performs no live route comparisons, snapshots, native writes, or completeness checks.
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(texture);
        using var target = StaticGpuFramebuffer.Create([image]);
        using var incoming = CreateRenderTarget(1, 1, TextureFormat.Rgba32f);
        incoming.Bind();
        var pass = FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0, Load:AttachmentLoad.Clear, Clear: ColorClearValue.Float(.25f, .5f, .75f, 1))]);
        Assert.Throws<InvalidOperationException>(() => FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex: -1)]));
        RenderPassTestBoundary.Run(commands => { commands.BeginRenderPass(pass); commands.EndRenderPass(); });
        OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
        using (var work = new FramebufferRoutingWorkCapture())
        {
            if (standalone)
            {
                RenderPassTestBoundary.Run(boundary =>
                {
                    for (int step = 0; step < 3; step++)
                    {
                        boundary.BeginRenderPass(pass); boundary.EndRenderPass();
                    }
                });
            }
            else {
    var commands=RHICommandContext.Current;
    using var scope76=commands.EnterBoundary(new("Tests.StaticFramebuffer",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope76.Run(()=>{
                for (int step = 0; step < 3; step++)
                {
                    commands.BeginRenderPass(pass);
                    commands.EndRenderPass();
                }
            });
}
            Assert.Equal(0, work.DynamicApplications);
            Assert.Equal(0, work.Captures);
            Assert.Equal(0, work.CompletenessChecks);
            output.WriteLine("STATIC_FRAMEBUFFER_WORK=" + System.Text.Json.JsonSerializer.Serialize(new { standalone, work.DynamicApplications, work.Captures,  work.CompletenessChecks }));
        }
        Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(.25f, texture.ReadTexel()[0]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
