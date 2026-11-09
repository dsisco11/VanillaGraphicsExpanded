using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks texture snapshot publication and source allocation dependencies.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuTextureMetadataPublicationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A retired manager is replaced before allocation, retaining view dependency checks.</summary>
    [Fact]
    public void RetiredManagerIsReplacedBeforeTextureCreation()
    {
        // Arrange: Retire the context manager before creating a source allocation and its dependent view.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            // Act: Create the source under a replacement manager, then change its selected mip range and allocation.
            RHIResourceManagerSystem.CurrentManager!.Dispose();
            Assert.True(RHIResourceManagerSystem.CurrentManager.IsDisposed);
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 4), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            // Assert: The new manager registers the source and view dependencies track storage replacement, not mip selection.
            Assert.NotNull(RHIResourceManagerSystem.CurrentManager.GetResourceRegistration(source));
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
            source.SetMipRange(0, 0);
            Assert.True(view.IsValid);
            Assert.True(RHIResourceManagerSystem.CurrentManager!.IsTextureViewSourceCurrent(view, source));
            var beforeImages = source.StorageImages;
            Assert.True(source.Resize(8, 4, 4));
            Assert.False(RHIResourceManagerSystem.CurrentManager!.IsTextureViewSourceCurrent(view, source));
            Assert.Equal(8, source.StorageImages[0].Width);
            Assert.False(beforeImages == source.StorageImages);
            Assert.False(view.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>View creation publishes the selected target and layer range.</summary>
    [Fact]
    public void ViewCreationPublishesSelectedParameters()
    {
        // Arrange: Create a live array source with four layers before requesting a narrower view.
        fixture.EnsureContextValid();
        using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 4), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        Assert.True(GL.IsTexture(source.TextureId));

        // Act: Create a view selecting only layers one and two.
        using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 1, LayerCount = 2, Label = null }).RequireResource();
        // Assert: The published view reports the requested target and selected layer range.
        Assert.True(view.IsValid);
        Assert.Equal(TextureDimension.TwoArray, view.Target);
        Assert.Equal(1, view.BaseLayer); Assert.Equal(2, view.LayerCount);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Snapshot replacement preserves a view, while source replacement or retirement invalidates it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextLocalViewTracksAllocationRatherThanSnapshot(bool retire)
    {
        // Arrange: Start without a manager snapshot, then create a source and dependent view in the recovered context.
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        Assert.Null(RHIResourceManagerSystem.CurrentManager);
        using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 4), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
        var before = source.StorageImages;
        // Act: Change only the mip-range snapshot, then either retire or replace the source allocation.
        source.SetMipRange(0, 0);
        Assert.True(before.SequenceEqual(source.StorageImages));
        Assert.True(view.IsValid);
        if (retire) source.Dispose();
        else Assert.True(source.Resize(8, 4, 4));
        // Assert: Snapshot-only change preserves the view, but allocation retirement or replacement invalidates it.
        Assert.False(view.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }


/// <summary>Named acquisition preserves sentinel selectors and pending native errors.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedMetadataQueriesPreserveState(bool immutable)
    {
        // Arrange: Bind a sentinel array texture and leave an unrelated native error pending before named acquisition.
        fixture.EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = 4, Height = 4, Depth = 4, MipLevels = 1, Storage = immutable ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable }).RequireResource();
        using var sentinel = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = 2, Height = 2, Depth = 2, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
        using var binding = OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)sentinel).Target), 0, sentinel.TextureId);
        GL.Enable((EnableCap)(-1));
        // Act: Acquire metadata by name for mutable or immutable storage without changing the selected binding.
        Assert.True(new GpuTextureMetadataReader(TextureDimension.TwoArray).TryRead(texture.TextureId, out var data));
        // Assert: Report allocation facts while preserving the sentinel selector and pending GL error.
        Assert.Equal(4, data.Levels[0].Width);
        Assert.Equal(immutable, data.ImmutableStorage);
        Assert.Equal(immutable ? 1 : 0, data.ImmutableLevels);
        if (immutable) Assert.NotNull(data.View); else Assert.Null(data.View);
        Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2DArray));
        Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Declines one authoritative snapshot without altering native command execution.</summary>
    private sealed class FailedRead : IGpuResourceMetadataReader<GpuTextureMetadata>
    {
        #region Public API

        public int Reads { get; private set; }
        /// <inheritdoc />
        public bool IsResource(nint id) => true;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuTextureMetadata metadata)
        {
            Reads++;
            metadata = default;
            return false;
        }
        #endregion
    }
    #endregion
}
