using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies retained external selections against common metadata and binding ownership.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PersistentBorrowedTextureTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Failed admission releases its provisional subscription while the external object survives.</summary>
    [Fact]
    public void FailedInitializationReleasesRecipient()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        BorrowedTexture? selected = null;
        int id = GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, id);
            Assert.Throws<ArgumentException>(() => BorrowedTexture.GetOrReplace(ref selected, id));
            Assert.Null(selected); Assert.True(GL.IsTexture(id));
            var entry = RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, id);
            Assert.True(entry is null || entry.AssociationCount == 0);
        }
        finally { selected?.Dispose(); RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, id)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Retiring the manager lifetime invalidates retained references without deleting external storage.</summary>
    [Fact]
    public void ContextRetirementRequiresNewSelection()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        BorrowedTexture? selected = null;
        int id = GL.GenTexture();
        try
        {
            Allocate(id, 4);
            var first = BorrowedTexture.GetOrReplace(ref selected, id);
            fixture.CleanupGpuResources();
            Assert.False(first.IsValid); Assert.True(GL.IsTexture(id));
            fixture.InitializeResourceDisposal();
            // An observed identity from a retired manager cannot be rediscovered
            // merely because its numeric name is still present in this fixture.
            Assert.Throws<InvalidOperationException>(() => BorrowedTexture.GetOrReplace(ref selected, id));
        }
        finally { selected?.Dispose(); RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, id)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Stable selection preserves wrapper identity, receives mutations and binds its current native allocation.</summary>
    [Fact]
    public void StableSelectionPreservesPublishedMetadataAndNativeBinding()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        BorrowedTexture? selected = null;
        try
        {
            using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            var first = BorrowedTexture.GetOrReplace(ref selected, owner.TextureId);
            for (int index = 0; index < 8; index++)
                Assert.Same(first, BorrowedTexture.GetOrReplace(ref selected, owner.TextureId));
            owner.Resize(8, 6);
            Assert.Same(first, BorrowedTexture.GetOrReplace(ref selected, owner.TextureId));
            Assert.Equal(8, first.Width); Assert.Equal(6, first.Height);
            OpenGlStateCache.Current.Invalidate(EPipelineState.TextureBindings);
            OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture2D, 0, first.TextureId);
            OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture2D, 0, first.TextureId);
            Assert.Equal(first.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
        }
        finally { selected?.Dispose(); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Selection changes retire only borrowers; unavailable snapshots recover on explicit acquisition.</summary>
    [Fact]
    public void ReplacementAndRecoveryPreserveExternalOwnership()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        BorrowedTexture? selected = null;
        try
        {
            using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var other = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 6), Height = System.Math.Max(1, 6), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            var first = BorrowedTexture.GetOrReplace(ref selected, owner.TextureId);
            RHIResourceManager.InvalidateWrapperMetadata(owner, GpuMetadataChange.Storage);
            Assert.Throws<InvalidOperationException>(() => BorrowedTexture.GetOrReplace(ref selected, owner.TextureId));
            RHIResourceManager.RecoverWrapperMetadata(owner, new GpuTextureMetadataReader(TextureDimension.Two));
            Assert.Same(first, BorrowedTexture.GetOrReplace(ref selected, owner.TextureId));
            var second = BorrowedTexture.GetOrReplace(ref selected, other.TextureId);
            Assert.NotSame(first, second); Assert.False(first.IsValid);
            Assert.True(GL.IsTexture(owner.TextureId));
            selected.Dispose(); selected = null;
            Assert.True(GL.IsTexture(other.TextureId));
            Assert.Throws<ArgumentOutOfRangeException>(() => BorrowedTexture.GetOrReplace(ref selected, 0));
            Assert.Null(selected);
            Assert.Equal(6, BorrowedTexture.GetOrReplace(ref selected, other.TextureId).Width);
        }
        finally { selected?.Dispose(); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Observed deletion makes the old borrower unusable before a numeric name is reused.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletedNameCannotReuseOldBorrower(bool reuseId)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        BorrowedTexture? selected = null;
        int id = GL.GenTexture();
        try
        {
            Allocate(id, 4);
            var first = BorrowedTexture.GetOrReplace(ref selected, id);
            // Isolate same-name registry replacement from driver-specific free-name allocation policy.
            if (reuseId)
            {
                var manager = RHIResourceManagerSystem.CurrentManager!;
                manager.ObserveDeletion(GpuResourceKind.Texture, id);
                // Model the new generation receipt for this synthetic same-name reuse.
                manager.ObserveImageCreation(GpuResourceKind.Texture, id, false);
            }
            else { RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, id)); id = GL.GenTexture(); }
            Assert.False(first.IsValid);
            Allocate(id, 8);
            var replacement = BorrowedTexture.GetOrReplace(ref selected, id);
            Assert.NotSame(first, replacement); Assert.Equal(8, replacement.Width);
            first.Dispose(); Assert.True(GL.IsTexture(id));
        }
        finally { selected?.Dispose(); RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, id)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Creates compatibility-profile external storage without pretending the raw command is covered.</summary>
    private static void Allocate(int id, int width)
    {
        GL.BindTexture(TextureTarget.Texture2D, id);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, width, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        OpenGlStateCache.Current.Invalidate(EPipelineState.TextureBindings);
    }

    #endregion
}
