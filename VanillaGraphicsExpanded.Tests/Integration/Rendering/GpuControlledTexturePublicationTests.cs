using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using HarmonyLib;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks controlled texture publication, native results and storage revisions.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuControlledTexturePublicationTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Allocation fallback and rejection
    /// <summary>Multisample publication records the sample count returned by native storage allocation.</summary>
    [Fact]
    public void MultisampleAllocationPublishesActualSampleCount()
    {
            // Arrange: reset per-test resources and create an output recipient for multisample allocation.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var original = GpuSupport.Graphics;
        try
        {
            // Arrange the standard multisample request and observe its recipient.
            using var recipient = new TextureRecipient();
            var request = new GpuTextureInitialization
            {
                Target = TextureDimension.TwoMultisample, AllocationMode = GpuTextureAllocationMode.Mutable,
                InternalFormat = TextureFormat.Rgba8, Width = 8, Height = 8, Levels = 1,
                Samples = 3, FixedSampleLocations = true
            };
            // Act: allocate multisample storage and read the sample count returned by OpenGL.
            var result = RHIResourceManager.CreateTexture(request, recipient, out var handle);
            Assert.True(result.HasValue);
            GL.GetTextureLevelParameter((int)handle.Id, 0, GetTextureParameter.TextureSamples, out int native);
            // Assert: the published sample count matches the sample count allocated by native OpenGL.
            Assert.Equal(native, result.Value.Levels[0].Samples);
            var error = GL.GetError();
            Assert.Equal(ErrorCode.NoError, error);
        }
        finally
        {
            AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, original);
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>An illegal external image level preserves the native error and does not issue an illegal level read.</summary>
    [Fact]
    public void RejectedExternalImageLevelPreservesNativeError()
    {
        // Arrange: bind a one-level texture whose dimensions provide a stable pre-call snapshot.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 8), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var binding = OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)texture).Target), 0, texture.TextureId);
            // Act: submit an invalid image level that must preserve its native error.
            GL.TexImage2D(TextureTarget.Texture2D, int.MaxValue, PixelInternalFormat.Rgba8,
                1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            // Assert: the rejected call leaves published dimensions intact and reports only its expected error.
            Assert.Equal(8, texture.Width);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A later mip allocation interruption retires the private name without handing out partial metadata.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedMipAllocationRetiresUnpublishedTexture(bool registered)
    {
        // Arrange: interrupt the second allocation command while keeping disposal tracking conditional by row.
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new TextureRecipient();
            var request = new GpuTextureInitialization
            {
                Target = TextureDimension.Two, AllocationMode = GpuTextureAllocationMode.Mutable,
                InternalFormat = TextureFormat.Rgba8, Width = 8, Height = 8, Levels = 3
            };
            using var fault = new AllocationFailure();
            GpuResourceHandle handle = default;
            // Act: run the allocation whose second native image command is interrupted.
            Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateTexture(request, recipient, out handle));
            // Assert: the partial texture is retired without publishing a handle or recipient identity.
            Assert.NotEqual(0, fault.Texture);
            Assert.Equal(2, fault.Calls);
            Assert.Equal(default, handle);
            Assert.Equal((nint)0, recipient.ResourceId);
            RHIResourceManagerSystem.CurrentManager?.OnRenderFrame(0, RHIResourceManager.Stage);
            Assert.False(GL.IsTexture(fault.Texture));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion

    #region External storage and content revisions

    /// <summary>External parameter and single-image changes retain unrelated images and storage revisions.</summary>
    [Fact]
    public void ExternalChangesPreserveUnaffectedDescriptors()
    {
        // Arrange: retain the original mip snapshots and storage revision before changing native storage.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 16), Height = System.Math.Max(1, 8), MipLevels = 4, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (4) - 1, Label = null }).RequireResource();

            using var binding = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, texture.TextureId);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(texture));
            var original = entry.Metadata.Levels;
            ulong storage = entry.StorageRevision;
            // Act: change the mip range; the storage revision and image descriptors must remain stable.
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 2);
            // Assert: parameter changes update only the selected range metadata.
            Assert.Equal(original, entry.Metadata.Levels);
            Assert.Equal(2, entry.Metadata.MaxLevel);
            Assert.Equal(storage, entry.StorageRevision);
            // Act: redefine only mip two with a different format and extent.
            GL.TexImage2D(TextureTarget.Texture2D, 2, PixelInternalFormat.Rgba16f,
                4, 2, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            // Assert: neighboring levels remain untouched while the storage revision advances once.
            Assert.Equal(original[0], entry.Metadata.Levels[0]);
            Assert.Equal(original[1], entry.Metadata.Levels[1]);
            Assert.Equal(original[3], entry.Metadata.Levels[3]);
            Assert.Equal(TextureFormat.Rgba16f, entry.Metadata.Levels[2].Format);
            Assert.Equal(storage + 1, entry.StorageRevision);
            GL.GetTextureLevelParameter(texture.TextureId, 2, GetTextureParameter.TextureInternalFormat, out int format);
            Assert.Equal((int)PixelInternalFormat.Rgba16f, format);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Immutable generation is content-only; mutable generation advances storage only when images change.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenerationDistinguishesAllocationFromContents(bool immutable)
    {
        // Arrange: create mutable and immutable storage variants with the same selected mip range.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new TextureRecipient();
            var request = new GpuTextureInitialization
            {
                Target = TextureDimension.Two, AllocationMode = immutable ? GpuTextureAllocationMode.Immutable : GpuTextureAllocationMode.Mutable,
                InternalFormat = TextureFormat.Rgba8, Width = 8, Height = 8, Levels = immutable ? 4 : 1, MaxLevel = 3
            };
            // Act: create the selected storage variant before checking later content-only generation.
            Assert.NotNull(RHIResourceManager.CreateTexture(request, recipient, out var handle));
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(recipient));
            using var binding = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, (int)handle.Id);
            ulong storage = entry.StorageRevision;
            // Act: generate the mip chain twice to separate allocation from content updates.
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            // Assert: generated level metadata is complete and immutable storage retains its storage revision.
            Assert.Equal(4, entry.Metadata.Levels.Length);
            Assert.Equal(storage + (immutable ? 0UL : 1UL), entry.StorageRevision);
            storage = entry.StorageRevision;
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            // Assert: repeating content generation leaves storage unchanged and produces the expected tail level.
            Assert.Equal(storage, entry.StorageRevision);
            GL.GetTextureLevelParameter((int)handle.Id, 3, GetTextureParameter.TextureWidth, out int lastWidth);
            Assert.Equal(1, lastWidth);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion

    #region Multisample creation policy

    /// <summary>Multisample requests require explicit sample-location intent and publish a capability-resolved sample count.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultisampleCreationValidatesIntentAndPublishesSamples(bool immutable)
    {
        // Arrange: prepare both storage modes with sample locations left unspecified.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new TextureRecipient();
            var request = new GpuTextureInitialization
            {
                Target = TextureDimension.TwoMultisample,
                AllocationMode = immutable ? GpuTextureAllocationMode.Immutable : GpuTextureAllocationMode.Mutable,
                InternalFormat = TextureFormat.Rgba8, Width = 8, Height = 4, Levels = 1, Samples = 1
            };
            // Act: reject missing sample-location intent, then retry after specifying it.
            Assert.Throws<ArgumentException>(() => RHIResourceManager.CreateTexture(request, recipient, out _));
            Assert.Equal((nint)0, recipient.ResourceId);
            request.FixedSampleLocations = false;
            var result = RHIResourceManager.CreateTexture(request, recipient, out var handle);
            // Assert: the accepted allocation publishes the native dimensions and resolved sample count.
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
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion

    #region Mip and view descriptions

    /// <summary>Sized texture creation and mip selection preserve complete image metadata.</summary>
    [Fact]
    public void CreationAndMipRangePublishCompleteMetadata()
    {
        // Arrange: create a four-level texture and retain its complete image snapshot and storage revision.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {

            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 16), Height = System.Math.Max(1, 8), MipLevels = 4, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (4) - 1, Label = null }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(texture));
            Assert.Equal(4, entry.Metadata.Levels.Length);
            Assert.Equal(16, entry.Metadata.Levels[0].Width);
            Assert.Equal(2, entry.Metadata.Levels[3].Width);
            Assert.Equal(1, entry.Metadata.Levels[3].Height);
            var originalImages = entry.Metadata.Levels;
            ulong storage = entry.StorageRevision;
            // Act: narrow the native base/max mip range without reallocating storage.
            texture.SetMipRange(1, 2);
            // Assert: selected range metadata changes while image facts, storage revision, and native values agree.
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
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A view publishes dimensions and selected layer range matching its native storage.</summary>
    [Fact]
    public void TextureViewPublishesDerivedImages()
    {
        // Arrange: create an array-backed source whose selected view spans the middle three layers.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 6), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            // Act: create the view and query its published image facts.
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 2, LayerCount = 3, Label = null }).RequireResource();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(view));
            // Assert: derived dimensions and native storage agree with the source layer selection.
            Assert.Equal(new GpuTextureViewMetadata(0, 1, 2, 3), entry.Metadata.View);
            var image = Assert.Single(entry.Metadata.Levels);
            Assert.Equal(8, image.Width);
            Assert.Equal(4, image.Height);
            Assert.Equal(3, image.Depth);
            Assert.Equal(TextureFormat.Rgba32f, image.Format);
            GL.GetTextureLevelParameter(view.TextureId, 0, GetTextureParameter.TextureWidth, out int width);
            GL.GetTextureLevelParameter(view.TextureId, 0, GetTextureParameter.TextureDepth, out int depth);
            Assert.Equal(image.Width, width);
            Assert.Equal(image.Depth, depth);
            Assert.True(view.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #endregion

    #region Private
    /// <summary>Interrupts allocation after the second real native image command without an ABI exception.</summary>
    private sealed unsafe class AllocationFailure : IDisposable
    {
        private readonly NativeDriverCapture native = new();
        internal int Texture;
        internal int Calls;

        #region Public API

        /// <summary>Restores the original loader and reports contained test-observation failures.</summary>
        public void Dispose() => native.Dispose();

        #endregion

        #region Internal API

        /// <summary>Observes actual allocation dispatch and interrupts its existing managed completion guard.</summary>
        internal AllocationFailure()
        {
            nint original = native.Original("glTexImage2D");
            native.Add("glTexImage2D", new Image2D((target, level, internalFormat, width, height, border, format, type, data) =>
            {
                ((delegate* unmanaged[Stdcall]<int, int, int, int, int, int, int, int, void*, void>)original)
                    (target, level, internalFormat, width, height, border, format, type, data);
                native.Observe(() =>
                {
                    Calls++;
                    Texture = GL.GetInteger(GetPName.TextureBinding2D);
                    // Native work has completed. The next managed step must reject certification
                    // of this interrupted candidate and retire the unpublished allocation.
                    if (Calls == 2)
                        OpenGlStateCache.Current.InterruptNativeResourceOperation(GpuResourceKind.Texture, Texture);
                });
            }));
            native.Install();
        }

        #endregion

        #region Private

        /// <summary>Matches the native mutable two-dimensional image-definition ABI.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void Image2D(int target, int level, int internalFormat, int width, int height,
            int border, int format, int type, void* data);

        #endregion
    }

    /// <summary>Receives the manager-created texture identity and facts without adding wrapper allocation policy.</summary>
    private sealed class TextureRecipient : GpuResource<GpuTextureMetadata>
    {
        #region Public API

        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }

        #endregion
    }

    #endregion
}
