using VanillaGraphicsExpanded.Rendering.RHI;
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
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource();
            int offset = Math.Max(1, GpuSupport.Graphics.TextureBufferOffsetAlignment);
            buffer.Allocate(checked(offset + 512));
            using var view = ranged
                ? RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = RHIFormat.R32f, WholeBuffer = false, Offset = offset, Length = 256, Label = null }).RequireResource()
                : RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = RHIFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
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
            using var owner = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = (true ? RHIStorageMode.Immutable : RHIStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
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
