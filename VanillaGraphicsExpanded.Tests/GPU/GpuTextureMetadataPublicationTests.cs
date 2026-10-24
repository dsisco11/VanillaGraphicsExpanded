using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks texture snapshot publication and source allocation dependencies.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuTextureMetadataPublicationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A retired installed manager does not suppress context-local allocation dependency checks.</summary>
    [Fact]
    public void RetiredManagerLeavesContextLocalDependencyValidationActive()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            GpuResourceManagerSystem.CurrentManager!.Dispose();
            Assert.True(GpuResourceManagerSystem.CurrentManager.IsDisposed);
            using var source = DynamicTexture3D.Create(4, 4, 4, PixelInternalFormat.Rgba32f, immutableStorage: true);
            Assert.Null(GpuResourceManagerSystem.CurrentManager.GetResourceRegistration(source));
            using var view = GpuTextureView.CreateFromTexture(source, TextureTarget.Texture2DArray, PixelInternalFormat.Rgba32f, 0, 1, 0, 1);
            source.SetMipRange(0, 0);
            Assert.True(view.IsValid);
            Assert.Null(GpuResourceManagerSystem.CurrentManager!.IsTextureViewSourceCurrent(view, source));
            var beforeImages = source.StorageImages;
            Assert.True(source.Resize(8, 4, 4));
            Assert.Null(GpuResourceManagerSystem.CurrentManager!.IsTextureViewSourceCurrent(view, source));
            Assert.Equal(8, source.StorageImages[0].Width);
            Assert.False(beforeImages == source.StorageImages);
            Assert.False(view.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>View creation reads one complete snapshot through either native query path.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ViewCreationQueriesEachParameterOnce(bool dsa)
    {
        fixture.EnsureContextValid();
        using var source = DynamicTexture3D.Create(4, 4, 4, PixelInternalFormat.Rgba32f, immutableStorage: true);
        using var calibration = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
        using var observer = new ResourceCallObserver(forceEnabled: true);
        observer.Calibrate(calibration.TextureId);
        Assert.True(GL.IsTexture(source.TextureId));
        Assert.Equal(1, observer.Calls("IsTexture"));
        using var capabilities = new DirectStateAccessCapabilityScope(dsa);
        observer.Reset();
        using var view = GpuTextureView.CreateFromTexture(source, TextureTarget.Texture2DArray, PixelInternalFormat.Rgba32f, 0, 1, 1, 2);
        Assert.True(view.IsValid);
        Assert.Equal(1, observer.Calls("IsTexture"));
        foreach (var parameter in new[] { GetTextureParameter.TextureViewMinLevel, GetTextureParameter.TextureViewNumLevels,
                     GetTextureParameter.TextureViewMinLayer, GetTextureParameter.TextureViewNumLayers })
            Assert.Equal(1, observer.ParameterCalls(parameter));
        Assert.Equal(dsa ? 0 : 8, observer.Calls("GetTexParameter"));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Snapshot replacement preserves a view, while source replacement or retirement invalidates it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextLocalViewTracksAllocationRatherThanSnapshot(bool retire)
    {
        fixture.EnsureContextValid();
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        using var source = DynamicTexture3D.Create(4, 4, 4, PixelInternalFormat.Rgba32f, immutableStorage: true);
        using var view = GpuTextureView.CreateFromTexture(source, TextureTarget.Texture2DArray, PixelInternalFormat.Rgba32f, 0, 1, 0, 1);
        var before = source.StorageImages;
        source.SetMipRange(0, 0);
        Assert.True(before.SequenceEqual(source.StorageImages));
        Assert.True(view.IsValid);
        if (retire) source.Dispose();
        else Assert.True(source.Resize(8, 4, 4));
        Assert.False(view.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A failed authoritative refresh escapes the public API and a later successful refresh recovers availability.</summary>
    [Fact]
    public void MipRangePublicationFailureCanRecover()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var source = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(source));
            var original = entry.Reader;
            var failure = new FailedRead();
            try
            {
                entry.Reader = failure;
                Assert.Throws<InvalidOperationException>(() => source.SetMipRange(0, 0));
                Assert.Equal(1, failure.Reads);
                Assert.False(entry.MetadataAvailable);
            }
            finally { entry.Reader = original; }
            source.SetMipRange(0, 0);
            Assert.True(entry.MetadataAvailable);
            Assert.True(source.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Forced capability subsets acquire native image facts without consuming errors or changing bindings.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void MetadataQueriesRespectOptionalCapabilities(bool dsa, bool storage, bool view)
    {
        fixture.EnsureContextValid();
        using var texture = DynamicTexture3D.Create(4, 4, 4, PixelInternalFormat.Rgba32f, immutableStorage: storage);
        using var sentinel = DynamicTexture3D.Create(2, 2, 2, PixelInternalFormat.Rgba32f);
        using var binding = sentinel.BindScope(0);
        using var calibration = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
        using var observer = new ResourceCallObserver(forceEnabled: true);
        observer.Calibrate(calibration.TextureId);
        var original = GpuSupport.Graphics;
        try
        {
            // These are selection tests on modern hardware, not older-driver qualification.
            AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, original with
            { SupportsArbDirectStateAccess = dsa, SupportsTextureStorage = storage, SupportsTextureView = view });
            GL.Enable((EnableCap)(-1));
            Assert.True(new GpuTextureMetadataReader(TextureTarget.Texture2DArray).TryRead(texture.TextureId, out var data));
            Assert.Equal(4, data.Levels[0].Width);
            Assert.Equal(storage, data.ImmutableStorage);
            Assert.Equal(storage ? 1 : 0, data.ImmutableLevels);
            Assert.Equal(storage ? 1 : 0, observer.ParameterCalls((GetTextureParameter)0x912F));
            Assert.Equal(view ? 1 : 0, observer.ParameterCalls((GetTextureParameter)0x82DF));
            foreach (var parameter in new[] { GetTextureParameter.TextureViewMinLevel, GetTextureParameter.TextureViewNumLevels,
                         GetTextureParameter.TextureViewMinLayer, GetTextureParameter.TextureViewNumLayers })
                Assert.Equal(view && storage ? 1 : 0, observer.ParameterCalls(parameter));
            if (!view) Assert.Null(data.View);
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBinding2DArray));
            Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, original); }
    }
    #endregion

    #region Private
    /// <summary>Declines one authoritative snapshot without altering native command execution.</summary>
    private sealed class FailedRead : IGpuResourceMetadataReader<GpuTextureMetadata>
    {
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
    }
    #endregion
}
