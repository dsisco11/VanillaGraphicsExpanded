using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Exercises consumer references across metadata changes and owner retirement.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuTextureReferenceTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Retained consumer values reject detached owners and retired manager contexts.</summary>
    [Fact]
    public void DetachAndContextRetirementInvalidateRetainedReferences()
    {
        // Arrange: Retain a value reference across owner detach, then attach the released native handle to a borrower.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int detached = 0;
        try
        {
            using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            GpuTextureRef reference = owner;
            // Act: Detach the owner, then retire resource services while the borrower still holds the raw name.
            detached = (int)owner.Detach();
            Assert.False(reference.IsEmpty);
            Assert.False(reference.IsValid);
            Assert.Throws<InvalidOperationException>(() => reference.TextureId);
            Assert.True(GL.IsTexture(detached));
            using var borrower = new BorrowedTexture(detached);
            GpuTextureRef borrowedReference = borrower;
            Assert.True(borrowedReference.IsValid);
            fixture.RetireResourceServices();
            // Assert: References reject access after their owner context retires without deleting the external name.
            Assert.False(borrowedReference.IsValid);
            Assert.Throws<InvalidOperationException>(() => borrowedReference.TextureId);
            Assert.True(GL.IsTexture(detached));
        }
        finally
        {
            if (detached != 0) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, detached));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }


/// <summary>Retained references observe new facts while borrower disposal preserves the owner.</summary>
    [Fact]
    public void MetadataRecoveryAndBorrowerRetirementUseManagerAuthority()
    {
        // Arrange: Keep owner and borrower references while tracking allocation identity and metadata availability.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var borrowed = new BorrowedTexture(texture.TextureId);
            GpuTextureRef owner = texture;
            GpuTextureRef alias = borrowed;
            int hash = owner.GetHashCode();
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1024; index++)
            {
                GpuTextureRef copy = texture;
                hash ^= copy.GetHashCode();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            Assert.Equal(0, allocated);
            Assert.Equal(owner.GetHashCode(), hash);
            // Retain the value across a resize to check that identity stays stable while manager metadata changes.
            var retained = owner;
            // Act: Resize the owner, withdraw its metadata, and request recovery through the resource manager.
            texture.Resize(8, 6);
            // Assert: Value identity stays stable while recovered metadata follows the new allocation dimensions.
            Assert.Equal(retained, owner);
            Assert.True(owner.TryGetMetadata(out var data));
            Assert.Contains(data.Levels, level => level.Level == 0 && level.Width == 8 && level.Height == 6);
            // Explicit withdrawal makes metadata unavailable until the manager's reader is asked to recover it.
            RHIResourceManager.InvalidateWrapperMetadata(texture);
            Assert.False(owner.TryGetMetadata(out _));
            Assert.False(alias.IsValid);
            Assert.False(texture.IsValid);
            Assert.Throws<InvalidOperationException>(() => RHIResourceManager.ValidateTextureReferenceInput(owner.Resource));
            Assert.Throws<InvalidOperationException>(() => RHIResourceManager.AcquireWrapperMetadata(texture, GpuTextureMetadataReader.Instance));
            RHIResourceManager.RecoverWrapperMetadata(texture, GpuTextureMetadataReader.Instance);
            // Assert: Recovery restores both references; disposing the borrower then invalidates only that alias.
            Assert.True(owner.IsValid);
            Assert.True(alias.IsValid);
            borrowed.Dispose();
            Assert.False(alias.IsEmpty);
            Assert.False(alias.IsValid);
            Assert.True(owner.IsValid);
            Assert.True(GL.IsTexture(owner.TextureId));
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
}
