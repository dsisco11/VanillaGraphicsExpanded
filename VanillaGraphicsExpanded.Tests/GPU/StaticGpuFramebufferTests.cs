using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies immutable framebuffer configuration, construction proof and repeated-pass fast paths.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class StaticGpuFramebufferTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    #region Configuration and lifetime
    /// <summary>Only the dynamic subclass exposes mutable factories; the shared base cannot be constructed.</summary>
    [Fact]
    public void SubclassFactoriesCannotSilentlyConstructTheWrongKind()
    {
        Assert.True(typeof(GpuFramebuffer).IsAbstract);
        Assert.Equal(typeof(GpuFramebuffer), typeof(StaticGpuFramebuffer).BaseType);
        Assert.Equal(typeof(GpuFramebuffer), typeof(DynamicGpuFramebuffer).BaseType);
        Assert.DoesNotContain(typeof(StaticGpuFramebuffer).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy), method => method.Name is "CreateEmpty" or "Wrap" or "CreateMRT");
        Assert.Equal(typeof(DynamicGpuFramebuffer), typeof(DynamicGpuFramebuffer).GetMethod("CreateEmpty")!.DeclaringType);
    }
    /// <summary>Attachment aliases cannot alter frozen allocations, while uploads into existing storage remain valid.</summary>
    [Fact]
    public void FrozenAttachmentsRejectAliasedReconfigurationButPermitContents()
    {
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var attachment = GpuFramebufferAttachment.FromTexture(texture);
        using var target = StaticGpuFramebuffer.Create([attachment]);
        using var alias = DynamicGpuFramebuffer.Wrap(target.FboId, width: 2, height: 2);
        Assert.Throws<InvalidOperationException>(() => target.Resize(3, 3));
        Assert.Throws<InvalidOperationException>(() => texture.Resize(3, 3));
        Assert.Throws<InvalidOperationException>(texture.Dispose);
        Assert.Throws<InvalidOperationException>(attachment.Dispose);
        Assert.Throws<InvalidOperationException>(() => alias.SetAttachment(FramebufferSlot.Color0, attachment));
        Assert.Throws<InvalidOperationException>(() => alias.RemoveAttachment(FramebufferSlot.Color0));
        Assert.Throws<InvalidOperationException>(() => alias.Resize(3, 3));
        Assert.Throws<InvalidOperationException>(() => VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, target.FboId)));
        target.Bind();
        Assert.Throws<InvalidOperationException>(() => OpenGLFramebufferRouting.SetDrawBuffer(OpenGlStateCache.Current, DrawBufferMode.None));
        texture.UploadDataImmediate(Enumerable.Repeat(.375f, 16).ToArray());
        Assert.All(texture.ReadPixels(), value => Assert.Equal(.375f, value));
        target.Dispose();
        Assert.True(texture.Resize(3, 3));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Static view attachments retain both the view and source allocation until configuration retirement.</summary>
    [Fact]
    public void StaticArrayViewFreezesSourceAndDetachReleasesConfiguration()
    {
        EnsureContextValid();
        using var array = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 3), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = array, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 3, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(view, layer: 1);
        using var target = StaticGpuFramebuffer.Create([image]);
        Assert.Throws<InvalidOperationException>(() => array.Resize(3, 3, 3));
        Assert.Throws<InvalidOperationException>(view.Dispose);
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
        EnsureContextValid();
        // Other owning-image fixtures may retain their shared disposal service.
        // This case installs its own manager to exercise its actual queue drain.
        fixture.CleanupGpuResources();
        using var manager = new GpuResourceManager();
        GpuResourceManagerSystem.Initialize(manager);
        try
        {
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
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
            OpenGlStateCache.Current.RequireMutableFramebufferConfiguration(id);
            Assert.True(texture.Resize(3, 3));
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.False(GL.IsFramebuffer(id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GpuResourceManagerSystem.Shutdown();
            TextureStreamingSystem.Dispose();
        }
    }

    /// <summary>A retired context cannot keep immutable authority over an identical name in a new registration.</summary>
    [Fact]
    public void ContextRetirementWithdrawsStaticNativeIdentity()
    {
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
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
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
        using (var work = new FramebufferRoutingWorkCapture()) using (var calls = new GraphicsTransitionCapture())
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
            Assert.DoesNotContain(calls.Calls, call => call.StartsWith("Routing"));
            output.WriteLine("STATIC_FRAMEBUFFER_WORK=" + System.Text.Json.JsonSerializer.Serialize(new { standalone, work.DynamicApplications, work.Captures,  work.CompletenessChecks, calls = calls.Calls }));
        }
        Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(.25f, texture.ReadPixels()[0]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #endregion
}
