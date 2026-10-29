using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks external renderbuffer allocation and manager-owned image dependency publication.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed unsafe class GpuRenderbufferMetadataInterceptionTests(HeadlessGLFixture fixture)
{
    private static int[]? capturedAdvancedArguments;
    private static int renderbufferBinds;
    /// <summary>Matches the renderbuffer selector ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BindRenderbufferCall(int target, uint name);
    /// <summary>Matches the optional named coverage allocation ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CoverageStorageCall(uint name, int coverage, int samples, int format, int width, int height);
    #region Public API
    /// <summary>An interrupted texture storage operation withdraws all selected images and notifies their consumers before recovery.</summary>
    [Fact]
    public void AbortedTextureStorageOperationInvalidatesDependents()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var first = GpuFramebufferAttachment.FromTexture(texture);
            using var second = GpuFramebufferAttachment.FromTexture(texture);
            using var firstFramebuffer = DynamicGpuFramebuffer.Create([first])!;
            using var secondFramebuffer = DynamicGpuFramebuffer.Create([second])!;
            int firstChanges = 0, secondChanges = 0;
            firstFramebuffer.AttachmentsChanged += () => firstChanges++;
            secondFramebuffer.AttachmentsChanged += () => secondChanges++;
            using (OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)texture).Target), 0, texture.TextureId))
            using (var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Storage))
                new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current).DefineTextureImages(texture.TextureId, new GpuTextureInitialization { Target = TextureDimension.Two, AllocationMode = GpuTextureAllocationMode.Mutable, InternalFormat = TextureFormat.Rgba8, Width = 8, Height = 4, Levels = 1 }.Resolve(), operation);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            Assert.False(manager.IsResourceMetadataAvailable(first));
            Assert.False(manager.IsResourceMetadataAvailable(second));
            Assert.Equal(0, texture.Width);
            Assert.Equal(1, firstChanges);
            Assert.Equal(1, secondChanges);
            Assert.Throws<InvalidOperationException>(() => firstFramebuffer.Bind());
            Assert.Throws<InvalidOperationException>(() => secondFramebuffer.Bind());
            RHIResourceManager.RecoverWrapperMetadata(texture, new GpuTextureMetadataReader(TextureDimension.Two));
            Assert.Equal(2, firstChanges);
            Assert.Equal(2, secondChanges);
            Assert.Equal(8, first.Width);
            Assert.Equal(8, second.Width);
            firstFramebuffer.Bind();
            secondFramebuffer.Bind();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Creation, storage and metadata queries preserve sentinel selection; direct operations issue no bind calls.</summary>
    [Fact]
    public void OwnedStorageUsesCoreNamedAddressing()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        using var native = new NativeDriverCapture();
        var originalBind = (delegate* unmanaged[Stdcall]<int, uint, void>)native.Original("glBindRenderbuffer");
        native.Add("glBindRenderbuffer", new BindRenderbufferCall((target, name) =>
        {
            originalBind(target, name);
            renderbufferBinds++;
        }));
        native.Install();
        int sentinel = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, sentinel);
        OpenGlStateCache.Current.InvalidateAll();
        try
        {
            renderbufferBinds = 0;
            using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 4, Label = null }).RequireResource();
            using var alias = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLRenderbufferIntegration.Borrow(owner.RenderbufferId);
            owner.Resize(8, 6);
            Assert.Equal(8, owner.Width);
            Assert.Equal(8, alias.Width);
            Assert.Equal(6, alias.Height);
            Assert.Equal(4, alias.Samples);
            Assert.Equal(sentinel, GL.GetInteger(GetPName.RenderbufferBinding));
            Assert.Equal(0, renderbufferBinds);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Renderbuffer, sentinel));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Checks unsupported extension dispatch through a controlled native substitute, without claiming extension execution.</summary>
    [Fact]
    public void AdvancedExtensionAdapterForwardsExactArguments()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        using var native = new NativeDriverCapture();
        var original = (delegate* unmanaged[Stdcall]<uint, int, int, int, int, void>)native.Original("glNamedRenderbufferStorageMultisample");
        native.Add("glNamedRenderbufferStorageMultisampleCoverageEXT", new CoverageStorageCall((name, coverage, samples, format, width, height) =>
        {
            capturedAdvancedArguments = [unchecked((int)name), coverage, samples, format, width, height];
            // The stand-in supplies ordinary native multisample storage; production observation
            // still runs through the optional entrypoint and its actual post-call ABI.
            original(name, samples, format, width, height);
        }));
        native.Install();
        try
        {
            using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
            var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
            ulong previous = entry.MetadataRevision;
            capturedAdvancedArguments = null;
            AllocateAdvanced(owner.RenderbufferId);
            Assert.Equal(new[] { owner.RenderbufferId, 4, 4, (int)InternalFormat.Rgba8, 12, 6 }, capturedAdvancedArguments);
            Assert.Equal(12, owner.Width);
            Assert.Equal(6, owner.Height);
            Assert.Equal(previous + 1, entry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { capturedAdvancedArguments = null; fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Bound and explicit core/EXT mutations publish aliases and selected images before notifying consumers once.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ExternalStoragePublishesImages(int variant)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch();
        try
        {
            using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
            using var alias = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLRenderbufferIntegration.Borrow(owner.RenderbufferId);
            using var image = GpuFramebufferAttachment.FromRenderbuffer(owner);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            int notifications = 0;
            framebuffer.AttachmentsChanged += () => { notifications++; Assert.Equal(8, image.Width); };
            var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
            ulong revision = entry.MetadataRevision;
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, owner.RenderbufferId);
            OpenGlStateCache.Current.InvalidateAll();
            if (variant == 0) GL.Enable((EnableCap)(-1));
            Allocate(variant, owner.RenderbufferId, 8);
            Assert.Equal(8, owner.Width);
            Assert.Equal(8, alias.Width);
            Assert.Equal(8, image.Width);
            Assert.Equal(1, notifications);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal(variant >= 4 ? 4 : 0, owner.Samples);
            if (variant == 0) Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
            framebuffer.Bind();
            Assert.Equal(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Rejected dimensions retain real storage facts and leave the native error for its existing consumer.</summary>
    [Fact]
    public void RejectedAllocationRetainsFacts()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch();
        try
        {
            using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromRenderbuffer(owner);
            OpenGLImageBindings.BindRenderbuffer(OpenGlStateCache.Current, owner.RenderbufferId);
            Allocate(0, owner.RenderbufferId, -1);
            Assert.Equal(4, owner.Width);
            Assert.Equal(4, image.Width);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Supplied storage facts reach all consumers without invoking a failing metadata reader.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeStorageBypassesUnavailableReader(bool throws)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch();
        try
        {
            using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
            using var first = GpuFramebufferAttachment.FromRenderbuffer(owner);
            using var second = GpuFramebufferAttachment.FromRenderbuffer(owner);
            using var firstFramebuffer = DynamicGpuFramebuffer.Create([first])!;
            using var secondFramebuffer = DynamicGpuFramebuffer.Create([second])!;
            int firstChanges = 0, secondChanges = 0;
            firstFramebuffer.AttachmentsChanged += () => firstChanges++;
            firstFramebuffer.AttachmentsChanged += () => throw new InvalidOperationException("Injected consumer failure.");
            secondFramebuffer.AttachmentsChanged += () => secondChanges++;
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuRenderbufferMetadata>>(manager.GetResourceRegistration(owner));
            var failingReader = new FailedReader(throws);
            entry.Reader = failingReader;
            Allocate(1, owner.RenderbufferId, 8);
            Assert.Equal(0, failingReader.Reads);
            Assert.True(manager.IsResourceMetadataAvailable(first));
            Assert.True(manager.IsResourceMetadataAvailable(second));
            Assert.Equal(1, firstChanges);
            Assert.Equal(1, secondChanges);
            Assert.Equal(8, first.Width);
            Assert.Equal(8, second.Width);
            firstFramebuffer.Bind();
            secondFramebuffer.Bind();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Owner resize publishes image changes once, including texture replacement that must move associations.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OwnedResizeNotifiesOnce(int kind)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var renderbuffer = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var replacement = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image = kind == 0 ? GpuFramebufferAttachment.FromRenderbuffer(renderbuffer)
                : kind == 1 ? GpuFramebufferAttachment.FromTexture(texture) : GpuFramebufferAttachment.FromTexture(replacement, layer: 0);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            int changes = 0;
            framebuffer.AttachmentsChanged += () => changes++;
            if (kind == 0) renderbuffer.Resize(8, 4);
            else if (kind == 1) texture.Resize(8, 4);
            else replacement.Resize(8, 4, 2);
            Assert.Equal(8, image.Width);
            Assert.Equal(1, changes);
            framebuffer.Bind();
            Assert.Equal(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private

    /// <summary>Exercises exact extension signatures while their unavailable hardware implementation is substituted.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateAdvanced(int id)
    {
        GL.Ext.NamedRenderbufferStorageMultisampleCoverage(id, 4, 4, InternalFormat.Rgba8, 12, 6);
    }

    /// <summary>Installs the production engine transpiler on this fixture's native call boundary.</summary>
    private static Harmony Patch()
    {
        var harmony = new Harmony("VGE.Tests.RenderbufferMetadata");

        return harmony;
    }

    /// <summary>Executes ordinary and multisample storage through core and EXT bound/named signatures.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Allocate(int variant, int id, int width)
    {
        switch (variant)
        {
            case 0: GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, width, 4); break;
            case 1: GL.NamedRenderbufferStorage(id, RenderbufferStorage.Rgba8, width, 4); break;
            case 2: GL.Ext.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, width, 4); break;
            case 3: GL.Ext.NamedRenderbufferStorage(id, RenderbufferStorage.Rgba8, width, 4); break;
            case 4: GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, 4, RenderbufferStorage.Rgba8, width, 4); break;
            case 5: GL.NamedRenderbufferStorageMultisample(id, 4, RenderbufferStorage.Rgba8, width, 4); break;
            case 6: GL.Ext.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, 4, RenderbufferStorage.Rgba8, width, 4); break;
            case 7: GL.Ext.NamedRenderbufferStorageMultisample(id, 4, RenderbufferStorage.Rgba8, width, 4); break;
        }
    }

    /// <summary>Injects an unavailable native metadata read without changing actual driver allocation behavior.</summary>
    private sealed class FailedReader(bool throws) : IGpuResourceMetadataReader<GpuRenderbufferMetadata>
    {
        internal int Reads { get; private set; }
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuRenderbufferMetadata metadata)
        {
            Reads++;
            metadata = default;
            if (throws) throw new InvalidOperationException("Injected renderbuffer read failure.");
            return false;
        }
    }
    #endregion
}
