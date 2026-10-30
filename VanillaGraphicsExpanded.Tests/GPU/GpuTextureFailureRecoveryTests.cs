using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises failed replacement and context-local snapshot failure through live resource APIs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuTextureFailureRecoveryTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Controlled replacement succeeds without descriptor recovery even when the native reader is unavailable.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReplacementPublishesWithoutNativeReader(bool managed, bool layered)
    {
        fixture.EnsureContextValid();
        if (managed) fixture.InitializeResourceDisposal();
        try
        {
            using GpuTexture texture = layered
                ? RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource()
                : new ReplacementTexture();
            using var borrower = managed ? new BorrowedTexture(texture.TextureId) : null;
            using var image = layered ? GpuFramebufferAttachment.FromTexture(texture, layer: 0)
                : GpuFramebufferAttachment.FromTexture(texture);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            var observations = new List<(int Width, bool Available)>();
            framebuffer.AttachmentsChanged += () => observations.Add((image.Width, ((GpuTextureRef)texture).IsValid));
            using var faults = new NativeReadFailure();
            faults.Enabled = true;
            if (texture is DynamicTexture3D array) array.Resize(8, 4, 2);
            else ((ReplacementTexture)texture).Replace();
            Assert.Equal((nint)0, faults.LastReadId);
            Assert.NotEqual(0, texture.TextureId);
            Assert.True(texture.IsValid);
            Assert.Equal(8, texture.Width);
            Assert.True(GL.IsTexture(texture.TextureId));
            Assert.Equal((8, true), Assert.Single(observations));
            if (borrower is not null) Assert.False(((GpuTextureRef)borrower).IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Failed replacement retires the original lifetime and reports unavailable images once.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedReplacementWithdrawsOriginalImages(bool layered)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.TextureReplacementCompletion");
        try
        {
            using GpuTexture texture = layered
                ? RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription
                    { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = 4, Height = 4,
                        Depth = 2, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource()
                : new ReplacementTexture();
            using var borrower = new BorrowedTexture(texture.TextureId);
            using var image = layered ? GpuFramebufferAttachment.FromTexture(texture, layer: 0)
                : GpuFramebufferAttachment.FromTexture(texture);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            var observations = new List<(int Width, bool Available)>();
            framebuffer.AttachmentsChanged += () => observations.Add((image.Width, ((GpuTextureRef)texture).IsValid));
            harmony.Patch(AccessTools.Method(typeof(OpenGLBackend), nameof(OpenGLBackend.InitializeTextureOwner)),
                prefix: new HarmonyMethod(typeof(GpuTextureFailureRecoveryTests), nameof(RejectReplacementInitialization)));
            Assert.Throws<ApplicationException>(() =>
            {
                if (texture is DynamicTexture3D array) array.Resize(8, 4, 2);
                else ((ReplacementTexture)texture).Replace();
            });
            Assert.Equal((nint)0, texture.ResourceId);
            Assert.False(((GpuTextureRef)borrower).IsValid);
            Assert.Equal((0, false), Assert.Single(observations));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>In-place storage callbacks retain exclusion until notification completes.</summary>
    [Fact]
    public void InPlaceResizeRejectsMutationFromItsCompletionCallback()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
                { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 4, Height = 4,
                    MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            int notifications = 0;
            Exception? reentry = null;
            framebuffer.AttachmentsChanged += () =>
            {
                // Attempt only once so a broken guard produces a bounded, observable failure.
                if (++notifications == 1) reentry = Record.Exception(() => { texture.Resize(6, 4); });
            };
            Assert.True(texture.Resize(8, 4));
            Assert.IsType<InvalidOperationException>(reentry);
            Assert.Equal(1, notifications);
            Assert.Equal(8, texture.Width);
            Assert.Equal(8, image.Width);
            Assert.True(((GpuTextureRef)texture).IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Controlled local mip selection keeps dimensions and availability without reading the descriptor.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextLocalMipRangeBypassesFailedReader(bool throws)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var faults = new NativeReadFailure { Enabled = true, Throw = throws };
        texture.SetMipRange(0, 0);
        Assert.Equal((nint)0, faults.LastReadId);
        Assert.Equal(4, texture.Width);
        Assert.Equal(4, texture.Height);
        Assert.True(((GpuTextureRef)texture).IsValid);
        RHICommandContext.Current.BindSampledTextures([new(0, texture)]);
        faults.Enabled = false;
        texture.SetMipRange(0, 0);
        Assert.Equal(4, texture.Width);
        Assert.True(((GpuTextureRef)texture).IsValid);
        RHICommandContext.Current.BindSampledTextures([new(0, texture)]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An interrupted operation hides its old descriptor until explicit recovery succeeds.</summary>
    [Fact]
    public void InterruptedContextLocalGroupWithdrawsSnapshotAndRecovers()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using (new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Metadata)) { }
        Assert.Equal(0, texture.Width);
        Assert.False(((GpuTextureRef)texture).IsValid);
        Assert.ThrowsAny<InvalidOperationException>(() => RHICommandContext.Current.BindSampledTextures([new(0, texture)]));
        RHIResourceManager.RecoverWrapperMetadata(texture, new GpuTextureMetadataReader(TextureDimension.Two));
        texture.SetMipRange(0, 0);
        Assert.Equal(4, texture.Width);
        Assert.True(((GpuTextureRef)texture).IsValid);
        RHICommandContext.Current.BindSampledTextures([new(0, texture)]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Framebuffer configuration cannot reuse a stale local projection after a native read fails.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalImageReadFailureClearsProjectionAndRecovers(bool throws)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(texture);
        using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
        using var faults = new NativeReadFailure { Enabled = true, Throw = throws };
        Assert.Throws<InvalidOperationException>(() => RHIResourceManager.RecoverWrapperMetadata(texture, GpuTextureMetadataReader.Instance));
        Assert.Throws<InvalidOperationException>(() => framebuffer.SetAttachment(FramebufferSlot.Color0, image));
        Assert.Equal(0, image.Width);
        Assert.Equal(0, image.Height);
        faults.Enabled = false;
        RHIResourceManager.RecoverWrapperMetadata(texture, GpuTextureMetadataReader.Instance);
        framebuffer.SetAttachment(FramebufferSlot.Color0, image);
        Assert.Equal(4, image.Width);
        Assert.True(framebuffer.CheckStatus(out _));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A selected mip removed by an external mutation cannot retain its old framebuffer dimensions.</summary>
    [Fact]
    public void MissingLocalImageSelectionClearsProjectionAndRecovers()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 2, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (2) - 1, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(texture, mipLevel: 1);
        using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
        using var binding = OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)texture).Target), 0, texture.TextureId);
        // Simulate an external owner withdrawing and later restoring the selected image.
        GL.TexImage2D(TextureTarget.Texture2D, 1, PixelInternalFormat.Rgba8, 0, 0, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        RHIResourceManager.RecoverWrapperMetadata(texture, GpuTextureMetadataReader.Instance);
        Assert.Throws<InvalidOperationException>(() => framebuffer.SetAttachment(FramebufferSlot.Color0, image));
        Assert.Equal(0, image.Width);
        GL.TexImage2D(TextureTarget.Texture2D, 1, PixelInternalFormat.Rgba8, 2, 2, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        RHIResourceManager.RecoverWrapperMetadata(texture, GpuTextureMetadataReader.Instance);
        framebuffer.SetAttachment(FramebufferSlot.Color0, image);
        Assert.Equal(2, image.Width);
        Assert.True(framebuffer.CheckStatus(out _));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Rejects candidate initialization after old storage retirement without altering native dispatch.</summary>
    private static void RejectReplacementInitialization() => throw new ApplicationException("Injected replacement initialization failure.");

    /// <summary>Exposes the owning replacement operation used by specialized two-dimensional textures.</summary>
    private sealed class ReplacementTexture : DynamicTexture2D
    {
        #region Public API
        /// <summary>Creates a small texture through the normal specialized-owner constructor.</summary>
        public ReplacementTexture() : base(4, 4, TextureFormat.Rgba8, TextureFilterMode.Nearest, null) { }
        /// <summary>Exercises replacement rather than the dynamic owner's separate in-place resize operation.</summary>
        public void Replace() => AllocateOrReallocate2DTexture(8, 4, TextureFormat.Rgba8, 1);
        #endregion
    }

    /// <summary>Injects native acquisition failure without changing native allocation or deletion behavior.</summary>
    private sealed class NativeReadFailure : IDisposable
    {
        private static NativeReadFailure? current;
        private readonly Harmony harmony = new("VGE.Tests.TextureFailureRecovery");
        public bool Enabled { get; set; }
        public bool Throw { get; set; }
        public nint LastReadId { get; private set; }
        public int Deletes { get; private set; }

        #region Public API
        /// <summary>Installs one collection-serialized reader fault and a deletion counter.</summary>
        public NativeReadFailure()
        {
            current = this;
            harmony.Patch(AccessTools.Method(typeof(GpuTextureMetadataReader), nameof(GpuTextureMetadataReader.TryRead)),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeReadFailure), nameof(Read))));
            harmony.Patch(AccessTools.Method(typeof(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend), nameof(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend.DeleteTexture), [typeof(int)]),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(NativeReadFailure), nameof(Delete))));
        }
        /// <summary>Restores the original methods before leaving the collection-serialized test.</summary>
        public void Dispose()
        {
            harmony.UnpatchAll(harmony.Id);
            current = null;
        }
        #endregion

        #region Private
        /// <summary>Records acquisition and optionally declines or throws before returning a snapshot.</summary>
        private static bool Read(nint id, ref GpuTextureMetadata metadata, ref bool __result)
        {
            var fault = current!;
            if (!fault.Enabled) return true;
            fault.LastReadId = id;
            if (fault.Throw) throw new InvalidOperationException("Injected native metadata acquisition failure.");
            metadata = default;
            __result = false;
            return false;
        }
        /// <summary>Counts deletion dispatch without suppressing the actual native deletion.</summary>
        private static void Delete() => current!.Deletes++;
        #endregion
    }
    #endregion
}
