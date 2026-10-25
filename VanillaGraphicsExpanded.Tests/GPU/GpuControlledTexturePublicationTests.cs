using OpenTK.Graphics.OpenGL;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks controlled texture publication against native outcomes while observing descriptor-query elimination.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuControlledTexturePublicationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>The approved multisample fallback reads only its actual sample count when format capabilities are unavailable.</summary>
    [Fact]
    public void MultisampleFallbackReadsOnlyActualSampleCount()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var original = GpuSupport.Graphics;
        try
        {
            using var calibration = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
            using var observer = new ResourceCallObserver(forceEnabled: true);
            observer.Calibrate(calibration.TextureId);
            AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, original with { SupportsInternalFormatQuery2 = false });
            using var recipient = new TextureRecipient();
            var request = new GpuTextureInitialization
            {
                Target = TextureTarget.Texture2DMultisample, AllocationMode = GpuTextureAllocationMode.Mutable,
                InternalFormat = PixelInternalFormat.Rgba8, Width = 8, Height = 8, Levels = 1,
                Samples = 3, FixedSampleLocations = true
            };
            var result = GpuResourceManager.CreateTexture(request, recipient, out var handle);
            Assert.True(result.HasValue);
            Assert.Equal(1, observer.Calls("GetTexLevelParameter"));
            Assert.Equal(0, observer.Calls("GetTextureLevelParameter"));
            Assert.Equal(0, observer.Calls("GetTextureParameter"));
            Assert.Equal(0, observer.Calls("GetTexParameter"));
            GL.GetTextureLevelParameter((int)handle.Id, 0, GetTextureParameter.TextureSamples, out int native);
            Assert.Equal(native, result.Value.Levels[0].Samples);
            var error = GL.GetError();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ms-sample-probe.txt"),
                $"Requested=3; Published={result.Value.Levels[0].Samples}; Native={native}; Error={error}; Renderer={GL.GetString(StringName.Renderer)}");
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
            using var texture = DynamicTexture2D.Create(8, 8, PixelInternalFormat.Rgba8);
            using var binding = texture.BindScope(0);
            StateCache.Current.ExecuteCoreTexImage2D(TextureTarget.Texture2D, int.MaxValue, PixelInternalFormat.Rgba8,
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

    /// <summary>Independent narrow recovery cannot query or publish while an explicit operation owns the texture.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NarrowRecoveryRejectsActiveOperationBeforeQueries(int recovery)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = DynamicTexture2D.CreateMipmapped(8, 8, PixelInternalFormat.Rgba8, 4);
            using var observer = new ResourceCallObserver(forceEnabled: true);
            observer.Calibrate(texture.TextureId);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(texture));
            ulong revision = entry.MetadataRevision;
            using var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Metadata);
            observer.Reset();
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (recovery == 0) manager.RefreshTextureParameter(entry, TextureParameterName.TextureBaseLevel);
                else if (recovery == 1) manager.RefreshTextureImages(entry, 0, 0, 0, false, true);
                else manager.RefreshGeneratedTextureMetadata(entry, true);
            });
            AssertNoDescriptorQueries(observer);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>External parameter and single-image changes retain unrelated images without a full descriptor scan.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalChangesQueryOnlyAffectedDescriptors(bool dsa)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var texture = DynamicTexture2D.CreateMipmapped(16, 8, PixelInternalFormat.Rgba8, 4);
            using var observer = new ResourceCallObserver(forceEnabled: true);
            observer.Calibrate(texture.TextureId);
            using var capabilities = new DirectStateAccessCapabilityScope(dsa);
            using var binding = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, texture.TextureId);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(texture));
            var original = entry.Metadata.Levels;
            ulong storage = entry.StorageRevision;
            observer.Reset();
            StateCache.Current.ExecuteCoreTexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 2);
            Assert.Equal(0, observer.Calls("GetTextureLevelParameter") + observer.Calls("GetTexLevelParameter"));
            Assert.Equal(1, observer.Calls("GetTextureParameter") + observer.Calls("GetTexParameter"));
            Assert.Equal(original, entry.Metadata.Levels);
            Assert.Equal(2, entry.Metadata.MaxLevel);
            Assert.Equal(storage, entry.StorageRevision);
            observer.Reset();
            StateCache.Current.ExecuteCoreTexImage2D(TextureTarget.Texture2D, 2, PixelInternalFormat.Rgba16f,
                4, 2, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            Assert.Equal(0, observer.Calls("GetTextureParameter") + observer.Calls("GetTexParameter"));
            Assert.InRange(observer.Calls("GetTextureLevelParameter") + observer.Calls("GetTexLevelParameter"), 1, 4);
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

    /// <summary>Immutable generation is content-only; mutable generation reads descendants and advances storage only when images change.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenerationDistinguishesAllocationFromContents(bool immutable)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var calibration = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
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
            using var observer = new ResourceCallObserver(forceEnabled: true);
            observer.Calibrate(calibration.TextureId);
            ulong storage = entry.StorageRevision;
            StateCache.Current.ExecuteCoreGenerateMipmap(GenerateMipmapTarget.Texture2D);
            Assert.Equal(4, entry.Metadata.Levels.Length);
            Assert.Equal(storage + (immutable ? 0UL : 1UL), entry.StorageRevision);
            if (immutable) AssertNoDescriptorQueries(observer);
            else
            {
                Assert.Equal(0, observer.Calls("GetTextureParameter") + observer.Calls("GetTexParameter"));
                Assert.InRange(observer.Calls("GetTextureLevelParameter") + observer.Calls("GetTexLevelParameter"), 1, 12);
            }
            storage = entry.StorageRevision;
            StateCache.Current.ExecuteCoreGenerateMipmap(GenerateMipmapTarget.Texture2D);
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

    /// <summary>Sized texture creation and mip selection preserve complete images without descriptor readback.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationAndMipRangePublishWithoutDescriptorQueries(bool dsa)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var calibration = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
            using var observer = new ResourceCallObserver(forceEnabled: true);
            observer.Calibrate(calibration.TextureId);
            using var capabilities = new DirectStateAccessCapabilityScope(dsa);
            using var texture = DynamicTexture2D.CreateMipmapped(16, 8, PixelInternalFormat.Rgba16f, 4);
            AssertNoDescriptorQueries(observer);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(texture));
            Assert.Equal(4, entry.Metadata.Levels.Length);
            Assert.Equal(16, entry.Metadata.Levels[0].Width);
            Assert.Equal(2, entry.Metadata.Levels[3].Width);
            Assert.Equal(1, entry.Metadata.Levels[3].Height);
            var originalImages = entry.Metadata.Levels;
            ulong storage = entry.StorageRevision;
            observer.Reset();
            texture.SetMipRange(1, 2);
            AssertNoDescriptorQueries(observer);
            Assert.Equal(1, entry.Metadata.BaseLevel);
            Assert.Equal(2, entry.Metadata.MaxLevel);
            Assert.Equal(originalImages, entry.Metadata.Levels);
            Assert.Equal(storage, entry.StorageRevision);
            // Native comparisons are deliberately outside the observed command intervals.
            GL.GetTextureParameter(texture.TextureId, GetTextureParameter.TextureBaseLevel, out int first);
            GL.GetTextureParameter(texture.TextureId, GetTextureParameter.TextureMaxLevel, out int last);
            Assert.Equal(1, first);
            Assert.Equal(2, last);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A view derives its dimensions and selected layer range from the known source storage without rereading it.</summary>
    [Fact]
    public void TextureViewPublishesDerivedImagesWithoutDescriptorQueries()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var calibration = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
            using var source = DynamicTexture3D.Create(8, 4, 6, PixelInternalFormat.Rgba32f, immutableStorage: true);
            using var observer = new ResourceCallObserver(forceEnabled: true);
            observer.Calibrate(calibration.TextureId);
            using var view = GpuTextureView.CreateFromTexture(source, TextureTarget.Texture2DArray,
                PixelInternalFormat.Rgba32f, 0, 1, 2, 3);
            AssertNoDescriptorQueries(observer);
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
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.TexImage2D),
                [typeof(TextureTarget), typeof(int), typeof(PixelInternalFormat), typeof(int), typeof(int), typeof(int), typeof(PixelFormat), typeof(PixelType), typeof(IntPtr)]),
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

    /// <summary>Checks the calibrated bound and named integer descriptor entry points used by the texture readers.</summary>
    private static void AssertNoDescriptorQueries(ResourceCallObserver observer)
    {
        Assert.Equal(0, observer.Calls("GetTextureParameter"));
        Assert.Equal(0, observer.Calls("GetTextureLevelParameter"));
        Assert.Equal(0, observer.Calls("GetTexParameter"));
        Assert.Equal(0, observer.Calls("GetTexLevelParameter"));
    }
    #endregion
}
