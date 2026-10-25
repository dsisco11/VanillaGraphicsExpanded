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
                ? DynamicTexture3D.Create(4, 4, 2, PixelInternalFormat.Rgba8)
                : new ReplacementTexture();
            using var faults = new NativeReadFailure();
            faults.Enabled = true;
            if (texture is DynamicTexture3D array) array.Resize(8, 4, 2);
            else ((ReplacementTexture)texture).Replace();
            Assert.Equal((nint)0, faults.LastReadId);
            Assert.NotEqual(0, texture.TextureId);
            Assert.True(texture.IsValid);
            Assert.Equal(8, texture.Width);
            Assert.True(GL.IsTexture(texture.TextureId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Controlled local mip selection keeps dimensions and availability without reading the descriptor.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextLocalMipRangeBypassesFailedReader(bool throws)
    {
        fixture.EnsureContextValid();
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        using var texture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using var faults = new NativeReadFailure { Enabled = true, Throw = throws };
        texture.SetMipRange(0, 0);
        Assert.Equal((nint)0, faults.LastReadId);
        Assert.Equal(4, texture.Width);
        Assert.Equal(4, texture.Height);
        Assert.True(texture.TryBind(0));
        faults.Enabled = false;
        texture.SetMipRange(0, 0);
        Assert.Equal(4, texture.Width);
        Assert.True(texture.TryBind(0));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An interrupted group discards its old local descriptor and does not prevent the next refresh.</summary>
    [Fact]
    public void InterruptedContextLocalGroupWithdrawsSnapshotAndRecovers()
    {
        fixture.EnsureContextValid();
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        using var texture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using (new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Metadata)) { }
        Assert.Equal(0, texture.Width);
        Assert.False(texture.TryBind(0));
        texture.SetMipRange(0, 0);
        Assert.Equal(4, texture.Width);
        Assert.True(texture.TryBind(0));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Framebuffer configuration cannot reuse a stale local projection after a native read fails.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalImageReadFailureClearsProjectionAndRecovers(bool throws)
    {
        fixture.EnsureContextValid();
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        using var texture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using var image = GpuFramebufferAttachment.FromTexture(texture);
        using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
        using var faults = new NativeReadFailure { Enabled = true, Throw = throws };
        Assert.Throws<InvalidOperationException>(() => framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment0, image));
        Assert.Equal(0, image.Width);
        Assert.Equal(0, image.Height);
        faults.Enabled = false;
        framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment0, image);
        Assert.Equal(4, image.Width);
        Assert.True(framebuffer.CheckStatus(out _));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A selected mip removed by an external mutation cannot retain its old framebuffer dimensions.</summary>
    [Fact]
    public void MissingLocalImageSelectionClearsProjectionAndRecovers()
    {
        fixture.EnsureContextValid();
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        using var texture = DynamicTexture2D.CreateMipmapped(4, 4, PixelInternalFormat.Rgba8, 2);
        using var image = GpuFramebufferAttachment.FromTexture(texture, mipLevel: 1);
        using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
        using var binding = texture.BindScope(0);
        // Simulate an external owner withdrawing and later restoring the selected image.
        GL.TexImage2D(TextureTarget.Texture2D, 1, PixelInternalFormat.Rgba8, 0, 0, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        Assert.Throws<InvalidOperationException>(() => framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment0, image));
        Assert.Equal(0, image.Width);
        GL.TexImage2D(TextureTarget.Texture2D, 1, PixelInternalFormat.Rgba8, 2, 2, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment0, image);
        Assert.Equal(2, image.Width);
        Assert.True(framebuffer.CheckStatus(out _));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Exposes the owning replacement operation used by specialized two-dimensional textures.</summary>
    private sealed class ReplacementTexture : DynamicTexture2D
    {
        #region Public API
        /// <summary>Creates a small texture through the normal specialized-owner constructor.</summary>
        public ReplacementTexture() : base(4, 4, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, null) { }
        /// <summary>Exercises replacement rather than the dynamic owner's separate in-place resize operation.</summary>
        public void Replace() => AllocateOrReallocate2DTexture(8, 4, PixelInternalFormat.Rgba8, 1);
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
            harmony.Patch(AccessTools.Method(typeof(StateCache), nameof(StateCache.DeleteTexture), [typeof(int)]),
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
