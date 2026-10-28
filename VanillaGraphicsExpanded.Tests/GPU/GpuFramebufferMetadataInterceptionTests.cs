using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks native framebuffer descriptor publication and lazy helper reconciliation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuFramebufferMetadataInterceptionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Attachment changes reach aliases and helpers without overwriting native configuration on the next bind.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void AttachmentMutationReconcilesHelpers(int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = Patch(); GL.CreateFramebuffers(1, out int readSentinel);
        try
        {
            using var first = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var second = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var volume = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 3), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var renderbuffer = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 8, Height = 4, Samples = 0, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(first);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = (GpuResourceMetadataEntry<GpuFramebufferMetadata>)manager.GetResourceRegistration(framebuffer)!;
            using var alias = new FramebufferAlias(framebuffer.FboId); manager.AssociateResource(entry, alias);
            ulong revision = framebuffer.AttachmentRevision;
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.DrawFramebuffer, framebuffer.FboId);
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.ReadFramebuffer, readSentinel);
            OpenGlStateCache.Current.InvalidateAll();
            using var capabilities = new DirectStateAccessCapabilityScope(variant != 9);
            int resource = variant is 4 or 5 ? renderbuffer.RenderbufferId : variant is 6 or 7 or 11 ? volume.TextureId : second.TextureId;
            Attach(variant, framebuffer.FboId, resource);
            Assert.Equal(framebuffer.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(readSentinel, GL.GetInteger(GetPName.ReadFramebufferBinding));
            var descriptor = alias.Snapshot.Attachments[FramebufferSlot.Color0];
            Assert.Equal(resource, descriptor.Id);
            Assert.Equal(variant is 4 or 5 ? GpuResourceKind.Renderbuffer : GpuResourceKind.Texture, descriptor.Kind);
            Assert.Equal(variant is 6 or 7 or 11 ? 1 : 0, descriptor.Layer);
            Assert.True(framebuffer.AttachmentRevision > revision);
            Assert.Equal(8, framebuffer.Width);
            framebuffer.Bind();
            GL.GetNamedFramebufferAttachmentParameter(framebuffer.FboId, FramebufferAttachment.ColorAttachment0,
                FramebufferParameterName.FramebufferAttachmentObjectName, out int actual);
            Assert.Equal(resource, actual);
            Assert.True(first.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Detach(framebuffer.FboId);
            Assert.Empty(alias.Snapshot.Attachments);
            Assert.True(manager.IsResourceMetadataAvailable(framebuffer));
            framebuffer.Bind();
            GL.GetNamedFramebufferAttachmentParameter(framebuffer.FboId, FramebufferAttachment.ColorAttachment0,
                FramebufferParameterName.FramebufferAttachmentObjectType, out int type);
            Assert.Equal((int)All.None, type);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, readSentinel)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A legal array layer beyond allocated storage remains a native attachment selection rather than an API rejection.</summary>
    [Fact]
    public void MissingArrayLayerPublishesSelectedDescriptor()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateFramebuffers(1, out int framebuffer);
        try
        {
            using var volume = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription
            {
                Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = 2, Height = 2,
                Depth = 2, MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest
            }).RequireResource();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = manager.AcquireResource(GpuResourceKind.Framebuffer, framebuffer, GpuFramebufferMetadataReader.Instance)!;
            using var alias = new FramebufferAlias(framebuffer);
            manager.AssociateResource(entry, alias);
            OpenGLFramebufferCalls.ExecuteCoreNamedFramebufferTextureLayer(framebuffer,
                FramebufferAttachment.ColorAttachment0, volume.TextureId, 0, 3);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            GL.GetNamedFramebufferAttachmentParameter(framebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferParameterName.FramebufferAttachmentTextureLayer, out int actualLayer);
            Assert.Equal(3, actualLayer);
            Assert.Equal(3, alias.Snapshot.Attachments[FramebufferSlot.Color0].Layer);
            Assert.True(manager.IsResourceMetadataAvailable(alias));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RHICommandContext.Current.RetireResource(new(GpuResourceKind.Framebuffer, framebuffer));
            fixture.CleanupGpuResources();
        }
    }

    /// <summary>Attachmentless defaults and rejected requests remain representable with split binding preservation.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void DefaultsAndRejectedCallsPreserveNativeFacts(int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = Patch(); GL.CreateFramebuffers(1, out int target); GL.CreateFramebuffers(1, out int sentinel);
        try
        {
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = manager.AcquireResource(GpuResourceKind.Framebuffer, target, GpuFramebufferMetadataReader.Instance)!;
            using var alias = new FramebufferAlias(target); manager.AssociateResource(entry, alias);
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.DrawFramebuffer, target);
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.ReadFramebuffer, sentinel);
            OpenGlStateCache.Current.InvalidateAll();
            using var capabilities = new DirectStateAccessCapabilityScope(variant != 3);
            SetDefault(variant, target, FramebufferDefaultParameter.FramebufferDefaultWidth, 17);
            SetDefault(variant, target, FramebufferDefaultParameter.FramebufferDefaultHeight, 9);
            Assert.Equal(17, alias.Snapshot.DefaultWidth); Assert.Equal(9, alias.Snapshot.DefaultHeight);
            Assert.Empty(alias.Snapshot.Attachments);
            Assert.True(manager.IsResourceMetadataAvailable(alias));
            SetDefault(variant, target, FramebufferDefaultParameter.FramebufferDefaultWidth, -1);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.Equal(17, alias.Snapshot.DefaultWidth);
            Assert.Equal(target, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(sentinel, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, target)); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, sentinel)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Unknown binding recovery preserves a pending error, and rejected image selection retains the real attachment.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AttachmentErrorsDoNotCorruptSnapshots(bool direct)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = Patch();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            var entry = (GpuResourceMetadataEntry<GpuFramebufferMetadata>)GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(framebuffer)!;
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.DrawFramebuffer, framebuffer.FboId);
            OpenGlStateCache.Current.InvalidateAll();
            using var capabilities = new DirectStateAccessCapabilityScope(direct);
            GL.Enable((EnableCap)(-1));
            Attach(0, framebuffer.FboId, texture.TextureId);
            Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
            var expected = entry.Metadata.Attachments[FramebufferSlot.Color0];
            RejectAttachment(framebuffer.FboId, texture.TextureId);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.Equal(expected, entry.Metadata.Attachments[FramebufferSlot.Color0]);
            Assert.True(GpuResourceManagerSystem.CurrentManager!.IsResourceMetadataAvailable(framebuffer));
            framebuffer.Bind();
            Assert.Equal(texture.TextureId, framebuffer.GetColorTextureId(0));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Packed depth and stencil native selections reconcile to one shared helper image.</summary>
    [Fact]
    public void PackedDepthStencilRetainsSharedHelperIdentity()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony=Patch();
        try
        {
            using var color=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image=GpuFramebufferAttachment.FromTexture(color);
            using var framebuffer=DynamicGpuFramebuffer.Create([image])!;
            using var packed=RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Depth24Stencil8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
            AttachPacked(framebuffer.FboId,packed.RenderbufferId);
            var depth=framebuffer.GetAttachment(FramebufferSlot.Depth);
            Assert.NotNull(depth);
            Assert.Same(depth,framebuffer.GetAttachment(FramebufferSlot.Stencil));
            Assert.Same(depth,framebuffer.GetAttachment(FramebufferSlot.DepthStencil));
            framebuffer.Bind();
            Assert.Equal(packed.RenderbufferId,depth.RenderbufferId);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>An unrepresented parameter produces its native error without refreshing canonical framebuffer descriptors.</summary>
    [Fact]
    public void UnknownDefaultParameterDoesNotRefreshMetadata()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony=Patch(); GL.CreateFramebuffers(1,out int framebuffer);
        try
        {
            var entry=GpuResourceManagerSystem.CurrentManager!.AcquireResource(GpuResourceKind.Framebuffer,framebuffer,GpuFramebufferMetadataReader.Instance)!;
            ulong revision=entry.MetadataRevision;
            SetDefault(1,framebuffer,(FramebufferDefaultParameter)(-1),1);
            Assert.Equal(ErrorCode.InvalidEnum,GL.GetError());
            Assert.Equal(revision,entry.MetadataRevision);
            Assert.True(entry.IsUsable);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, framebuffer)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>A changed originating context prevents observation and helper callbacks while leaving facts withdrawn.</summary>
    [Fact]
    public void CompletionInDifferentContextCannotNotifyHelpers()
    {
        fixture.EnsureContextValid();
        var origin=RenderContextRegistry.Current();
        var current=origin;
        using var manager=new GpuResourceManager(() => current, static _ => true);
        GpuResourceManagerSystem.Initialize(manager);
        try
        {
            using var texture=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image=GpuFramebufferAttachment.FromTexture(texture);
            using var framebuffer=DynamicGpuFramebuffer.Create([image])!;
            var entry=(GpuResourceMetadataEntry<GpuFramebufferMetadata>)manager.GetResourceRegistration(framebuffer)!;
            var reader=new CountingFramebufferReader(entry.Reader!); entry.Reader=reader;
            int callbacks=0; framebuffer.AttachmentsChanged+=() => callbacks++;
            ulong revision=entry.MetadataRevision;
            var observation=OpenGlStateCache.BeginNativeFramebufferMetadata(framebuffer.FboId,true);
            try
            {
                current=(origin.Item1,origin.Item2+1);
                Assert.Throws<InvalidOperationException>(() => observation.Complete(true));
                Assert.Equal(0,callbacks); Assert.Equal(0,reader.Reads);
                Assert.Equal(revision,entry.MetadataRevision); Assert.False(entry.MetadataAvailable);
            }
            finally { current=origin; }
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { manager.Dispose(); GpuResourceManagerSystem.Shutdown(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Installs production native call redirection on isolated fixture boundaries.</summary>
    private static Harmony Patch()
    {
        var harmony = new Harmony("VGE.Tests.FramebufferMetadataInterception");
        foreach (string name in new[] { nameof(Attach), nameof(Detach), nameof(SetDefault), nameof(RejectAttachment), nameof(AttachPacked) })
            harmony.Patch(AccessTools.Method(typeof(GpuFramebufferMetadataInterceptionTests), name),
                transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
        return harmony;
    }
    /// <summary>Exercises real bound, named and extension attachment forms including selected volume layers.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Attach(int variant, int framebuffer, int resource)
    {
        switch (variant)
        {
            case 0: GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, resource, 0); break;
            case 1: GL.NamedFramebufferTexture(framebuffer, FramebufferAttachment.ColorAttachment0, resource, 0); break;
            case 2: GL.Ext.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, resource, 0); break;
            case 3: GL.Ext.NamedFramebufferTexture2D(framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, resource, 0); break;
            case 4: GL.FramebufferRenderbuffer(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, resource); break;
            case 5: GL.NamedFramebufferRenderbuffer(framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, resource); break;
            case 6: GL.NamedFramebufferTextureLayer(framebuffer, FramebufferAttachment.ColorAttachment0, resource, 0, 1); break;
            case 7: GL.FramebufferTextureLayer(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, resource, 0, 1); break;
            case 8: GL.NamedFramebufferTexture((uint)framebuffer, FramebufferAttachment.ColorAttachment0, (uint)resource, 0); break;
            case 9: GL.FramebufferTexture(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, resource, 0); break;
            case 10: OpenGLFramebufferCalls.ExecuteCoreFramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, resource, 0); break;
            case 11: OpenGLFramebufferCalls.ExecuteCoreFramebufferTextureLayer(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, resource, 0, 1); break;
        }
    }
    /// <summary>Attaches one native packed image to both depth and stencil slots.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AttachPacked(int framebuffer,int renderbuffer) => GL.NamedFramebufferRenderbuffer(framebuffer,FramebufferAttachment.DepthStencilAttachment,RenderbufferTarget.Renderbuffer,renderbuffer);
    /// <summary>Issues an invalid mip selection while preserving the native rejection semantics.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RejectAttachment(int framebuffer, int texture) => GL.NamedFramebufferTexture(framebuffer, FramebufferAttachment.ColorAttachment0, texture, -1);
    /// <summary>Detaches the only image to retain an intentionally incomplete intermediate framebuffer.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Detach(int framebuffer) => GL.NamedFramebufferTexture(framebuffer, FramebufferAttachment.ColorAttachment0, 0, 0);
    /// <summary>Changes actual attachmentless defaults through capability-selected addressing.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetDefault(int variant, int framebuffer, FramebufferDefaultParameter parameter, int value)
    {
        if (variant == 1) GL.NamedFramebufferParameter(framebuffer, parameter, value);
        else if (variant == 2) GL.Ext.NamedFramebufferParameter(framebuffer, parameter, value);
        else GL.FramebufferParameter(FramebufferTarget.DrawFramebuffer, parameter, value);
    }
    /// <summary>Detects whether context rejection occurs before native framebuffer metadata acquisition.</summary>
    private sealed class CountingFramebufferReader(IGpuResourceMetadataReader<GpuFramebufferMetadata> inner) : IGpuResourceMetadataReader<GpuFramebufferMetadata>
    {
        internal int Reads;
        /// <inheritdoc />
        public bool IsResource(nint id) => inner.IsResource(id);
        /// <inheritdoc />
        public bool TryRead(nint id,out GpuFramebufferMetadata value) { Reads++; return inner.TryRead(id,out value); }
    }
    /// <summary>Receives canonical framebuffer facts independently of an owning helper.</summary>
    private sealed class FramebufferAlias(int id) : GpuResource<GpuFramebufferMetadata>
    {
        internal GpuFramebufferMetadata Snapshot => metadata;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; } = id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Framebuffer;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
    }
    #endregion
}




