using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks shared buffer-texture acquisition and alias-wide native replacement guards.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuResourceAliasMetadataTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Generic acquisition must preserve complete buffer-view metadata and shared availability.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BorrowerAcquisitionPreservesExistingBufferViewMetadata(bool ranged)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = GpuVbo.Create();
            int offset = Math.Max(1, GpuSupport.Graphics.TextureBufferOffsetAlignment);
            buffer.Allocate(checked(offset + 512));
            using var view = ranged
                ? GpuBufferView.CreateRange(buffer.BufferId, SizedInternalFormat.R32f, offset, 256)
                : GpuBufferView.CreateWholeBuffer(buffer.BufferId, SizedInternalFormat.R32f);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(view));
            Assert.NotNull(entry.Metadata.Buffer);
            Assert.Equal(buffer.BufferId, view.BufferId);
            var original = entry.Metadata.Buffer;
            var reader = entry.Reader;
            Assert.Throws<NotSupportedException>(() => new BorrowedTexture(view.TextureId));
            Assert.Same(reader, entry.Reader);
            Assert.Equal(original, entry.Metadata.Buffer);
            Assert.True(entry.MetadataAvailable);
            Assert.True(view.IsValid);

            Assert.Equal(buffer.BufferId, view.BufferId);
            Assert.Equal(SizedInternalFormat.R32f, view.Format);
            Assert.Equal((nint)(ranged ? offset : 0), view.OffsetBytes);
            Assert.Equal((nint)(ranged ? 256 : 0), view.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            while (GL.GetError() != ErrorCode.NoError) { }
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Owner and alias lifetime references reject native-name replacement before changing registration.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextureReplacementPreservesLifetimeRetainedAllocation(bool retainOwner)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var owner = DynamicTexture3D.Create(4, 4, 2, PixelInternalFormat.Rgba8, immutableStorage: true);
            using var borrower = new BorrowedTexture(owner.TextureId);
            GpuResource retained = retainOwner ? owner : borrower;
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var original = manager.GetResourceRegistration(owner)!;
            int originalId = owner.TextureId;
            retained.RetainLifetimeReference();
            try
            {
                Assert.Throws<InvalidOperationException>(() => owner.Resize(8, 4, 2));
                Assert.Equal(GpuResourceRegistrationState.Live, original.State);
                Assert.Same(original, manager.GetResourceRegistration(owner));
                Assert.Equal(originalId, owner.TextureId);
                Assert.True(borrower.IsValid);
                Assert.True(owner.IsValid);
                Assert.True(GL.IsTexture(originalId));
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
            finally { retained.ReleaseLifetimeReference(); }
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
}
