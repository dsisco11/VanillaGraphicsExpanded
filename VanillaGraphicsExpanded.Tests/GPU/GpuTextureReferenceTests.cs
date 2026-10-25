using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises consumer references across metadata changes and owner retirement.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuTextureReferenceTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Retained consumer values reject detached owners and retired manager contexts.</summary>
    [Fact]
    public void DetachAndContextRetirementInvalidateRetainedReferences()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int detached = 0;
        try
        {
            using var owner = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
            GpuTextureRef reference = owner;
            detached = (int)owner.Detach();
            Assert.False(reference.IsEmpty);
            Assert.False(reference.IsValid);
            Assert.Throws<InvalidOperationException>(() => reference.TextureId);
            Assert.True(GL.IsTexture(detached));
            using var borrower = new BorrowedTexture(detached);
            GpuTextureRef borrowedReference = borrower;
            Assert.True(borrowedReference.IsValid);
            fixture.CleanupGpuResources();
            Assert.False(borrowedReference.IsValid);
            Assert.Throws<InvalidOperationException>(() => borrowedReference.TextureId);
            Assert.True(GL.IsTexture(detached));
        }
        finally
        {
            if (detached != 0) StateCache.Current.DeleteTexture(detached);
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Owner replacement follows the wrapper while the old native view remains retired.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerReplacementPreservesReferenceButRejectsOldDependencies(bool managed)
    {
        fixture.EnsureContextValid();
        if (managed) fixture.InitializeResourceDisposal();
        try
        {
            using var owner = DynamicTexture3D.Create(2, 2, 4, PixelInternalFormat.Rgba32f, immutableStorage: true);
            using var view = GpuTextureView.CreateFromTexture(owner, TextureTarget.Texture2DArray,
                PixelInternalFormat.Rgba32f, 0, 1, 1, 2);
            GpuTextureRef reference = owner;
            GpuTextureRef viewReference = view;
            var before = reference;
            using (var image = GpuFramebufferAttachment.FromTexture(viewReference, layer: 1))
                Assert.Equal(view.TextureId, image.TextureId);
            Assert.Throws<ArgumentOutOfRangeException>(() => GpuFramebufferAttachment.FromTexture(viewReference, layer: 2));
            owner.Resize(4, 3, 4);
            Assert.Equal(before, reference);
            Assert.Equal(owner.TextureId, reference.TextureId);
            Assert.Equal(4, reference.RequireImage(0).Width);
            Assert.False(viewReference.IsValid);
            Assert.False(view.IsValid);
            Assert.Throws<InvalidOperationException>(() => viewReference.TextureId);
            Assert.False(GpuResourceManager.ValidateTextureReferenceInput(viewReference.Resource));
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Null conversions preserve empty semantics without accessing native state.</summary>
    [Fact]
    public void NullWrappersProduceEmptyReferences()
    {
        GpuTexture? texture = null;
        GpuTextureView? view = null;
        GpuTextureRef first = texture;
        GpuTextureRef second = view;
        Assert.True(first.IsEmpty);
        Assert.Equal(first, second);
        Assert.False(first.TryGetMetadata(out _));
        Assert.Throws<InvalidOperationException>(() => first.TextureId);
    }

    /// <summary>Retained references observe new facts while borrower disposal preserves the owner.</summary>
    [Fact]
    public void MetadataRecoveryAndBorrowerRetirementUseManagerAuthority()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
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
            var retained = owner;
            texture.Resize(8, 6);
            Assert.Equal(retained, owner);
            Assert.True(owner.TryGetMetadata(out var data));
            Assert.Contains(data.Levels, level => level.Level == 0 && level.Width == 8 && level.Height == 6);
            GpuResourceManager.InvalidateWrapperMetadata(texture);
            Assert.False(owner.TryGetMetadata(out _));
            Assert.False(alias.IsValid);
            Assert.False(texture.IsValid);
            Assert.Throws<InvalidOperationException>(() => GpuResourceManager.ValidateTextureReferenceInput(owner.Resource));
            GpuResourceManager.AcquireWrapperMetadata(texture, GpuTextureMetadataReader.Instance);
            Assert.True(owner.IsValid);
            Assert.True(alias.IsValid);
            borrowed.Dispose();
            Assert.False(alias.IsEmpty);
            Assert.False(alias.IsValid);
            Assert.True(owner.IsValid);
            Assert.True(GL.IsTexture(owner.TextureId));
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
}
