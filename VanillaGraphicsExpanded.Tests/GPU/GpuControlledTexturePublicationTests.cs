using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks controlled texture publication, native results and storage revisions.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuControlledTexturePublicationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>The multisample fallback publishes the native sample count when format capabilities are unavailable.</summary>
    [Fact]
    public void MultisampleFallbackPublishesActualSampleCount()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var original = GpuSupport.Graphics;
        try
        {
            // Sample selection uses the baseline per-format capability cache.
            using var recipient = new TextureRecipient();
            var request = new GpuTextureInitialization
            {
                Target = TextureTarget.Texture2DMultisample, AllocationMode = GpuTextureAllocationMode.Mutable,
                InternalFormat = PixelInternalFormat.Rgba8, Width = 8, Height = 8, Levels = 1,
                Samples = 3, FixedSampleLocations = true
            };
            var result = GpuResourceManager.CreateTexture(request, recipient, out var handle);
            Assert.True(result.HasValue);
            GL.GetTextureLevelParameter((int)handle.Id, 0, GetTextureParameter.TextureSamples, out int native);
            Assert.Equal(native, result.Value.Levels[0].Samples);
            var error = GL.GetError();
            Assert.Equal(ErrorCode.NoError, error);
        }
        finally
        {
            AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, original);
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>An illegal external image level preserves the native error and does not issue an illegal level read.</summary>
    [Fact]
    public void RejectedExternalImageLevelPreservesNativeError()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 8), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var binding = texture.BindScope(0);
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLTexturesCalls.ExecuteCoreTexImage2D(TextureTarget.Texture2D, int.MaxValue, PixelInternalFormat.Rgba8,
                1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            Assert.Equal(8, texture.Width);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A later mip allocation exception retires the private native name without handing out partial metadata.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedMipAllocationRetiresUnpublishedTexture(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new TextureRecipient();
            var request = new GpuTextureInitialization
            {
                Target = TextureTarget.Texture2D, AllocationMode = GpuTextureAllocationMode.Mutable,
                InternalFormat = PixelInternalFormat.Rgba8, Width = 8, Height = 8, Levels = 3
            };
            using var fault = new AllocationFailure();
            GpuResourceHandle handle = default;
            Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateTexture(request, recipient, out handle));
            Assert.NotEqual(0, fault.Texture);
            Assert.Equal(2, fault.Calls);
            Assert.Equal(default, handle);
            Assert.Equal((nint)0, recipient.ResourceId);
            GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.False(GL.IsTexture(fault.Texture));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Independent narrow recovery cannot publish while an explicit operation owns the texture.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NarrowRecoveryRejectsActiveOperation(int recovery)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 8), MipLevels = 4, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (4) - 1, Label = null }).RequireResource();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(texture));
            ulong revision = entry.MetadataRevision;
            using var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Metadata);
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (recovery == 0) manager.RefreshTextureParameter(entry, TextureParameterName.TextureBaseLevel);
                else if (recovery == 1) manager.RefreshTextureImages(entry, 0, 0, 0, false, true);
                else manager.RefreshGeneratedTextureMetadata(entry, true);
            });
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>External parameter and single-image changes retain unrelated images and storage revisions.</summary>
    [Fact]
    public void ExternalChangesPreserveUnaffectedDescriptors()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba8, Width = System.Math.Max(1, 16), Height = System.Math.Max(1, 8), MipLevels = 4, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (4) - 1, Label = null }).RequireResource();

            using var binding = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, texture.TextureId);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(texture));
            var original = entry.Metadata.Levels;
            ulong storage = entry.StorageRevision;
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLTexturesCalls.ExecuteCoreTexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 2);
            Assert.Equal(original, entry.Metadata.Levels);
            Assert.Equal(2, entry.Metadata.MaxLevel);
            Assert.Equal(storage, entry.StorageRevision);
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLTexturesCalls.ExecuteCoreTexImage2D(TextureTarget.Texture2D, 2, PixelInternalFormat.Rgba16f,
                4, 2, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            Assert.Equal(original[0], entry.Metadata.Levels[0]);
            Assert.Equal(original[1], entry.Metadata.Levels[1]);
            Assert.Equal(original[3], entry.Metadata.Levels[3]);
            Assert.Equal(PixelInternalFormat.Rgba16f, entry.Metadata.Levels[2].Format);
            Assert.Equal(storage + 1, entry.StorageRevision);
            GL.GetTextureLevelParameter(texture.TextureId, 2, GetTextureParameter.TextureInternalFormat, out int format);
            Assert.Equal((int)PixelInternalFormat.Rgba16f, format);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Immutable generation is content-only; mutable generation advances storage only when images change.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenerationDistinguishesAllocationFromContents(bool immutable)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new TextureRecipient();
            var request = new GpuTextureInitialization
            {
                Target = TextureTarget.Texture2D, AllocationMode = immutable ? GpuTextureAllocationMode.Immutable : GpuTextureAllocationMode.Mutable,
                InternalFormat = PixelInternalFormat.Rgba8, Width = 8, Height = 8, Levels = immutable ? 4 : 1, MaxLevel = 3
            };
            Assert.NotNull(GpuResourceManager.CreateTexture(request, recipient, out var handle));
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(recipient));
            using var binding = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, (int)handle.Id);
            ulong storage = entry.StorageRevision;
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLTexturesCalls.ExecuteCoreGenerateMipmap(GenerateMipmapTarget.Texture2D);
            Assert.Equal(4, entry.Metadata.Levels.Length);
            Assert.Equal(storage + (immutable ? 0UL : 1UL), entry.StorageRevision);
            storage = entry.StorageRevision;
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLTexturesCalls.ExecuteCoreGenerateMipmap(GenerateMipmapTarget.Texture2D);
            Assert.Equal(storage, entry.StorageRevision);
            GL.GetTextureLevelParameter((int)handle.Id, 3, GetTextureParameter.TextureWidth, out int lastWidth);
            Assert.Equal(1, lastWidth);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Multisample requests require explicit sample-location intent and publish a capability-resolved sample count.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultisampleCreationValidatesIntentAndPublishesSamples(bool immutable)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new TextureRecipient();
            var request = new GpuTextureInitialization
            {
                Target = TextureTarget.Texture2DMultisample,
                AllocationMode = immutable ? GpuTextureAllocationMode.Immutable : GpuTextureAllocationMode.Mutable,
                InternalFormat = PixelInternalFormat.Rgba8, Width = 8, Height = 4, Levels = 1, Samples = 1
            };
            Assert.Throws<ArgumentException>(() => GpuResourceManager.CreateTexture(request, recipient, out _));
            Assert.Equal((nint)0, recipient.ResourceId);
            request.FixedSampleLocations = false;
            var result = GpuResourceManager.CreateTexture(request, recipient, out var handle);
            Assert.True(result.HasValue);
            var image = Assert.Single(result.Value.Levels);
            Assert.Equal(8, image.Width);
            Assert.Equal(4, image.Height);
            Assert.Equal(0, result.Value.BaseLevel);
            Assert.Equal(0, result.Value.MaxLevel);
            GL.GetTextureLevelParameter((int)handle.Id, 0, GetTextureParameter.TextureSamples, out int samples);
            Assert.Equal(samples, image.Samples);
            Assert.True(samples >= 1);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Sized texture creation and mip selection preserve complete image metadata.</summary>
    [Fact]
    public void CreationAndMipRangePublishCompleteMetadata()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {

            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba16f, Width = System.Math.Max(1, 16), Height = System.Math.Max(1, 8), MipLevels = 4, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (4) - 1, Label = null }).RequireResource();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(texture));
            Assert.Equal(4, entry.Metadata.Levels.Length);
            Assert.Equal(16, entry.Metadata.Levels[0].Width);
            Assert.Equal(2, entry.Metadata.Levels[3].Width);
            Assert.Equal(1, entry.Metadata.Levels[3].Height);
            var originalImages = entry.Metadata.Levels;
            ulong storage = entry.StorageRevision;
            texture.SetMipRange(1, 2);
            Assert.Equal(1, entry.Metadata.BaseLevel);
            Assert.Equal(2, entry.Metadata.MaxLevel);
            Assert.Equal(originalImages, entry.Metadata.Levels);
            Assert.Equal(storage, entry.StorageRevision);
            GL.GetTextureParameter(texture.TextureId, GetTextureParameter.TextureBaseLevel, out int first);
            GL.GetTextureParameter(texture.TextureId, GetTextureParameter.TextureMaxLevel, out int last);
            Assert.Equal(1, first);
            Assert.Equal(2, last);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A view publishes dimensions and selected layer range matching its native storage.</summary>
    [Fact]
    public void TextureViewPublishesDerivedImages()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 6), MipLevels = 1, Storage = (true ? RHIStorageMode.Immutable : RHIStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 2, LayerCount = 3, Label = null }).RequireResource();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(view));
            Assert.Equal(new GpuTextureViewMetadata(0, 1, 2, 3), entry.Metadata.View);
            var image = Assert.Single(entry.Metadata.Levels);
            Assert.Equal(8, image.Width);
            Assert.Equal(4, image.Height);
            Assert.Equal(3, image.Depth);
            Assert.Equal(PixelInternalFormat.Rgba32f, image.Format);
            GL.GetTextureLevelParameter(view.TextureId, 0, GetTextureParameter.TextureWidth, out int width);
            GL.GetTextureLevelParameter(view.TextureId, 0, GetTextureParameter.TextureDepth, out int depth);
            Assert.Equal(image.Width, width);
            Assert.Equal(image.Depth, depth);
            Assert.True(view.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Throws on the second real mutable image command after the first image has established native storage.</summary>
    private sealed class AllocationFailure : IDisposable
    {
        private readonly Harmony harmony = new("VGE.Tests.ControlledTextureAllocationFailure");
        private static AllocationFailure? current;
        internal int Texture;
        internal int Calls;
        /// <summary>Installs the exact integer-level pointer overload used by mutable image allocation.</summary>
        internal AllocationFailure()
        {
            current = this;
            harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(OpenTK.Graphics.OpenGL4.GL.TexImage2D),
                [typeof(OpenTK.Graphics.OpenGL4.TextureTarget), typeof(int), typeof(OpenTK.Graphics.OpenGL4.PixelInternalFormat), typeof(int), typeof(int), typeof(int), typeof(OpenTK.Graphics.OpenGL4.PixelFormat), typeof(OpenTK.Graphics.OpenGL4.PixelType), typeof(IntPtr)]),
                prefix: new HarmonyMethod(typeof(AllocationFailure), nameof(BeforeImage)));
        }
        /// <summary>Removes only this test's native interception.</summary>
        public void Dispose() { harmony.UnpatchAll(harmony.Id); current = null; }
        /// <summary>Records the realized allocation before rejecting the second command.</summary>
        private static void BeforeImage()
        {
            var fault = current!;
            fault.Calls++;
            fault.Texture = GL.GetInteger(GetPName.TextureBinding2D);
            if (fault.Calls == 2) throw new InvalidOperationException("Injected image allocation failure.");
        }
    }

    /// <summary>Receives the manager-created texture identity and facts without adding wrapper allocation policy.</summary>
    private sealed class TextureRecipient : GpuResource<GpuTextureMetadata>
    {
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
    }

    #endregion
}
