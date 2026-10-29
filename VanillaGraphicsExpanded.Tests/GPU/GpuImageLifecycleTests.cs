using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies maintained native image lifetimes and query-free image consumer reuse.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed unsafe class GpuImageLifecycleTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>An unrecorded pre-install texture is not mistaken for a newly realized empty texture.</summary>
    [Fact]
    public void BindingUnknownPreinstallTextureDoesNotInventMetadata()
    {
        fixture.EnsureContextValid();
        OpenGLBindingInterception.Uninstall();
        GpuResourceManagerSystem.CurrentManager?.Dispose(); GpuResourceManagerSystem.Shutdown();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, 8, 4);
        var manager = GpuResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            Assert.True(GL.IsTexture(texture));
            Assert.Null(manager.FindResource(GpuResourceKind.Texture, texture));
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    /// <summary>Unknown pre-install framebuffer and renderbuffer binds do not create metadata.</summary>
    [Fact]
    public void BindingUnknownPreinstallFramebufferAndRenderbufferDoesNotInventMetadata()
    {
        fixture.EnsureContextValid();
        OpenGLBindingInterception.Uninstall();
        GpuResourceManagerSystem.CurrentManager?.Dispose(); GpuResourceManagerSystem.Shutdown();
        GL.CreateFramebuffers(1, out int framebuffer);
        GL.CreateRenderbuffers(1, out int renderbuffer);
        GL.NamedRenderbufferStorage(renderbuffer, RenderbufferStorage.Rgba8, 7, 5);
        var manager = GpuResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        try
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbuffer);
            Assert.Null(manager.FindResource(GpuResourceKind.Framebuffer, framebuffer));
            Assert.Null(manager.FindResource(GpuResourceKind.Renderbuffer, renderbuffer));
            Assert.False(OpenGlStateCache.Current.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out _));
            Assert.Null(OpenGlStateCache.Current.RenderbufferKnowledge);
        }
        finally { GL.DeleteFramebuffer(framebuffer); GL.DeleteRenderbuffer(renderbuffer); fixture.CleanupGpuResources(); }
    }

    /// <summary>A recorded pre-install texture is discovered once and later aliases reuse its original entry.</summary>
    [Fact]
    public void PreinstallTextureDiscoveryIsConsumedOnce()
    {
        fixture.EnsureContextValid();
        OpenGLBindingInterception.Uninstall();
        GpuResourceManagerSystem.CurrentManager?.Dispose(); GpuResourceManagerSystem.Shutdown();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, 8, 4);
        var manager = GpuResourceManagerSystem.EnsureCurrentContext();
        var original = manager.RecordPreexistingImage<GpuTextureMetadata>(GpuResourceKind.Texture, texture);
        OpenGLBindingInterception.Install();
        try
        {
            var reader = new PreinstallTextureReader();
            var observed = manager.AcquireResource(GpuResourceKind.Texture, texture, reader);
            Assert.Same(original, observed);
            Assert.Equal(1, reader.Reads);
            Assert.Equal(8, Assert.Single(original.Metadata.Levels).Width);
            Assert.Same(original, manager.AcquireResource(GpuResourceKind.Texture, texture, reader));
            Assert.Equal(1, reader.Reads);
            Assert.Throws<InvalidOperationException>(() => manager.RecordPreexistingImage<GpuTextureMetadata>(GpuResourceKind.Texture, texture));
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    /// <summary>The engine handoff admits pre-install framebuffer and texture identities without querying during capture.</summary>
    [Fact]
    public void EngineFramebufferHandoffRecordsPreinstallImages()
    {
        fixture.EnsureContextValid();
        OpenGLBindingInterception.Uninstall();
        GpuResourceManagerSystem.CurrentManager?.Dispose(); GpuResourceManagerSystem.Shutdown();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, 8, 4);
        GL.CreateFramebuffers(1, out int framebuffer);
        GL.NamedFramebufferTexture(framebuffer, FramebufferAttachment.ColorAttachment0, texture, 0);
        var manager = GpuResourceManagerSystem.EnsureCurrentContext();
        try
        {
            var reference = new FrameBufferRef { FboId = framebuffer, ColorTextureIds = [texture] };
            VanillaGraphicsExpanded.Rendering.Integration.EnginePreexistingImages.Record(manager, [reference, reference]);
            var originalTexture = manager.FindResource(GpuResourceKind.Texture, texture);
            var originalFramebuffer = manager.FindResource(GpuResourceKind.Framebuffer, framebuffer);
            Assert.NotNull(originalTexture); Assert.NotNull(originalFramebuffer);
            OpenGLBindingInterception.Install();
            using var reads = new ImageDescriptorCapture();
            var target = manager.AcquireResource(GpuResourceKind.Framebuffer, framebuffer, GpuFramebufferMetadataReader.Instance);
            var image = manager.AcquireResource(GpuResourceKind.Texture, texture, new GpuTextureMetadataReader(TextureDimension.Two));
            Assert.Same(originalTexture, image); Assert.Same(originalFramebuffer, target);
            var selected = target!.Metadata.Attachments[FramebufferSlot.Color0];
            Assert.Same(image, selected.OriginalBacking);
            Assert.True(image!.IsUsable);
            Assert.False(image.Metadata.Levels.IsDefaultOrEmpty);
            var projection = manager.ProjectFramebufferDescriptor(selected);
            Assert.True(projection.HasValue);
            Assert.Equal(8, projection.Value.Width);
            int initialReads = reads.Reads;
            Assert.True(initialReads > 0);
            Assert.Same(image, manager.AcquireResource(GpuResourceKind.Texture, texture, new GpuTextureMetadataReader(TextureDimension.Two)));
            Assert.Same(target, manager.AcquireResource(GpuResourceKind.Framebuffer, framebuffer, GpuFramebufferMetadataReader.Instance));
            Assert.Equal(initialReads, reads.Reads);
        }
        finally
        {
            OpenGLBindingInterception.Install();
            GL.DeleteFramebuffer(framebuffer); GL.DeleteTexture(texture); fixture.CleanupGpuResources();
        }
    }

    /// <summary>A proven pre-install renderbuffer admits one discovery and reuses its original identity.</summary>
    [Fact]
    public void PreinstallRenderbufferDiscoveryIsConsumedOnce()
    {
        fixture.EnsureContextValid();
        OpenGLBindingInterception.Uninstall();
        GpuResourceManagerSystem.CurrentManager?.Dispose(); GpuResourceManagerSystem.Shutdown();
        GL.CreateRenderbuffers(1, out int renderbuffer);
        GL.NamedRenderbufferStorage(renderbuffer, RenderbufferStorage.Rgba8, 7, 5);
        var manager = GpuResourceManagerSystem.EnsureCurrentContext();
        var original = manager.RecordPreexistingImage<GpuRenderbufferMetadata>(GpuResourceKind.Renderbuffer, renderbuffer);
        OpenGLBindingInterception.Install();
        try
        {
            using var reads = new ImageDescriptorCapture();
            var entry = manager.AcquireResource(GpuResourceKind.Renderbuffer, renderbuffer, GpuRenderbufferMetadataReader.Instance);
            Assert.Same(original, entry);
            Assert.Equal(7, entry!.Metadata.Width);
            int initialReads = reads.Reads;
            Assert.True(initialReads > 0);
            Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Renderbuffer, renderbuffer, GpuRenderbufferMetadataReader.Instance));
            Assert.Equal(initialReads, reads.Reads);
        }
        finally { GL.DeleteRenderbuffer(renderbuffer); fixture.CleanupGpuResources(); }
    }

    /// <summary>Deletion detaches a bound framebuffer and preserves the unbound attachment's original identity.</summary>
    [Fact]
    public void TextureDeletionUpdatesBoundFramebufferAndPreservesUnboundIdentity()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, 4, 4);
        GL.CreateFramebuffers(1, out int bound);
        GL.CreateFramebuffers(1, out int unbound);
        try
        {
            GL.NamedFramebufferTexture(bound, FramebufferAttachment.ColorAttachment0, texture, 0);
            GL.NamedFramebufferTexture(unbound, FramebufferAttachment.ColorAttachment0, texture, 0);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, bound);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var selected = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(manager.FindResource(GpuResourceKind.Framebuffer, bound));
            var other = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(manager.FindResource(GpuResourceKind.Framebuffer, unbound));
            GL.DeleteTexture(texture); texture = 0;
            Assert.True(selected.IsUsable);
            Assert.Empty(selected.Metadata.Attachments);
            Assert.True(other.IsUsable);
            Assert.Null(manager.ProjectFramebufferDescriptor(other.Metadata.Attachments[FramebufferSlot.Color0]));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.DeleteFramebuffer(bound); GL.DeleteFramebuffer(unbound);
            if (texture != 0) GL.DeleteTexture(texture);
            fixture.CleanupGpuResources();
        }
    }

    /// <summary>External backing deletion clears selected images and notifies each framebuffer once.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalDeletionWithdrawsFramebufferImageOnce(bool bound)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        try
        {
            GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, 4, 4);
            using var image = GpuFramebufferAttachment.FromTextureId(texture);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, bound ? framebuffer.FboId : 0);
            int changes = 0;
            framebuffer.AttachmentsChanged += () => changes++;
            GL.DeleteTexture(texture); texture = 0;
            Assert.False(image.IsValid);
            Assert.Equal(0, image.Width);
            Assert.Equal(1, changes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            if (texture != 0) GL.DeleteTexture(texture);
            fixture.CleanupGpuResources();
        }
    }

    /// <summary>An attachment call with the wrong entrypoint target cannot publish a rejected mutation.</summary>
    [Fact]
    public void FramebufferTexture1DRejectsTwoDimensionalTarget()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, 4, 4);
        GL.CreateFramebuffers(1, out int framebuffer);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Framebuffer, framebuffer));
            ulong revision = entry.MetadataRevision;
            var table = OpenGLBindingTable.For(typeof(GL));
            ((delegate* unmanaged[Stdcall]<int, int, int, uint, int, void>)table.Read("glFramebufferTexture1D"))(
                (int)FramebufferTarget.Framebuffer, (int)FramebufferAttachment.ColorAttachment0,
                (int)TextureTarget.Texture2D, (uint)texture, 0);
            Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Empty(entry.Metadata.Attachments);
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.DeleteFramebuffer(framebuffer); GL.DeleteTexture(texture); fixture.CleanupGpuResources();
        }
    }

    /// <summary>Pixel-unpack capacity rejection preserves existing facts while a valid upload publishes its definition.</summary>
    [Fact]
    public void PixelUnpackBufferBoundsControlDefinitionPublication()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GL.CreateBuffers(1, out int buffer);
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 4, 4, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, texture));
            GL.NamedBufferData(buffer, 64, nint.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, buffer);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
            GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
            ulong revision = entry.MetadataRevision;
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 8, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.True(entry.IsUsable);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(4, Assert.Single(entry.Metadata.Levels).Width);

            GL.NamedBufferData(buffer, 256, nint.Zero, BufferUsageHint.DynamicDraw);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 8, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            Assert.True(entry.IsUsable);
            Assert.Equal(8, Assert.Single(entry.Metadata.Levels).Width);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
            GL.DeleteBuffer(buffer); GL.DeleteTexture(texture); fixture.CleanupGpuResources();
        }
    }

    /// <summary>Mapped unpack buffers are accepted only when their mapping is persistent.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PixelUnpackMappingModeControlsDefinitionPublication(bool persistent)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GL.CreateBuffers(1, out int buffer);
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 4, 4, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            if (persistent)
                GL.NamedBufferStorage(buffer, 256, nint.Zero, BufferStorageFlags.MapWriteBit | BufferStorageFlags.MapPersistentBit);
            else GL.NamedBufferData(buffer, 256, nint.Zero, BufferUsageHint.DynamicDraw);
            var flags = BufferAccessMask.MapWriteBit | (persistent ? BufferAccessMask.MapPersistentBit : 0);
            Assert.NotEqual(nint.Zero, GL.MapNamedBufferRange(buffer, 0, 256, flags));
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, buffer);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
            GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, texture));
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 8, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            Assert.Equal(persistent ? ErrorCode.NoError : ErrorCode.InvalidOperation, GL.GetError());
            Assert.True(entry.IsUsable);
            Assert.Equal(persistent ? 8 : 4, Assert.Single(entry.Metadata.Levels).Width);
        }
        finally
        {
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
            GL.UnmapNamedBuffer(buffer);
            GL.DeleteBuffer(buffer); GL.DeleteTexture(texture); fixture.CleanupGpuResources();
        }
    }

    /// <summary>Both binding roots update texture-unit knowledge and withdraw affected image-slot knowledge.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeTextureMultiBindingsUpdateSelectedSlots(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        try
        {
            GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, 4, 4);
            var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
            var cache = OpenGlStateCache.Current;
            ((delegate* unmanaged[Stdcall]<uint, uint, void>)table.Read("glBindTextureUnit"))(3, (uint)texture);
            Assert.True(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 3, out int selected));
            Assert.Equal(texture, selected);
            ((delegate* unmanaged[Stdcall]<uint, int, uint*, void>)table.Read("glBindTextures"))(3, 1, null);
            Assert.True(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 3, out selected));
            Assert.Equal(0, selected);

            GL.BindImageTexture(1, texture, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba8);
            Assert.True(cache.TryGetCachedImageTexture(1, out selected));
            Assert.Equal(texture, selected);
            ((delegate* unmanaged[Stdcall]<uint, int, uint*, void>)table.Read("glBindImageTextures"))(1, 1, null);
            Assert.False(cache.TryGetCachedImageTexture(1, out _));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    /// <summary>Unsized format and multisample output reads remain narrow and occur once at allocation.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AllocationOutputReadsDoNotScanOtherDescriptors(bool multisample)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        // Ensure the multisample case exercises the fallback rather than a format
        // capability cached by an earlier test in the shared context.
        if (multisample) GpuSupport.Initialize(force: true);
        using var reads = new ImageDescriptorCapture();
        GL.CreateTextures(multisample ? TextureTarget.Texture2DMultisample : TextureTarget.Texture2D, 1, out int id);
        try
        {
            if (multisample) GL.TextureStorage2DMultisample(id, 3, SizedInternalFormat.Rgba8, 8, 4, true);
            else
            {
                GL.BindTexture(TextureTarget.Texture2D, id);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 8, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            }
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, id));
            Assert.True(entry.IsUsable); Assert.Equal(8, Assert.Single(entry.Metadata.Levels).Width);
            Assert.Equal(1, reads.Reads);
            Assert.Equal(multisample ? 1 : 0, reads.SampleReads);
            Assert.Equal(multisample ? 0 : 1, reads.FormatReads);
            using var borrowed = new BorrowedTexture(id);
            Assert.True(((GpuTextureRef)borrowed).IsValid); Assert.Equal(1, reads.Reads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Rejected pixel encodings and compressed sizes preserve the previous complete snapshot.</summary>
    [Fact]
    public void RejectedDefinitionsPreservePublishedFactsAndNativeErrors()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var reads = new ImageDescriptorCapture();
        int id = GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 4, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, id));
            ulong revision = entry.MetadataRevision;
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 8, 0, PixelFormat.RgbaInteger, PixelType.UnsignedByte, nint.Zero);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            GL.CompressedTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.CompressedRgbaBptcUnorm, 8, 8, 0, -1, nint.Zero);
            Assert.NotEqual(ErrorCode.NoError, GL.GetError());
            Assert.Equal(revision, entry.MetadataRevision); Assert.Equal(4, entry.Metadata.Levels[0].Width);
            Assert.True(entry.IsUsable); Assert.Equal(0, reads.Reads);
        }
        finally { GL.DeleteTexture(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>An invalid depth-stencil transfer encoding leaves the existing allocation certified.</summary>
    [Fact]
    public void RejectedDepthStencilEncodingPreservesPublishedFacts()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Depth24Stencil8, 4, 4, 0,
                PixelFormat.DepthStencil, PixelType.UnsignedInt248, nint.Zero);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, texture));
            ulong revision = entry.MetadataRevision;
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Depth24Stencil8, 8, 8, 0,
                PixelFormat.DepthStencil, PixelType.UnsignedByte, nint.Zero);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.True(entry.IsUsable);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(4, Assert.Single(entry.Metadata.Levels).Width);
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    /// <summary>Core ETC2/EAC compressed definitions replace mutable image facts without readback.</summary>
    [Theory]
    [InlineData(InternalFormat.CompressedR11Eac, TextureFormat.CompressedR11Eac, 8)]
    [InlineData(InternalFormat.CompressedSignedR11Eac, TextureFormat.CompressedSignedR11Eac, 8)]
    [InlineData(InternalFormat.CompressedRg11Eac, TextureFormat.CompressedRg11Eac, 16)]
    [InlineData(InternalFormat.CompressedSignedRg11Eac, TextureFormat.CompressedSignedRg11Eac, 16)]
    [InlineData(InternalFormat.CompressedRgb8Etc2, TextureFormat.CompressedRgb8Etc2, 8)]
    [InlineData(InternalFormat.CompressedSrgb8Etc2, TextureFormat.CompressedSrgb8Etc2, 8)]
    [InlineData(InternalFormat.CompressedRgb8PunchthroughAlpha1Etc2, TextureFormat.CompressedRgb8PunchthroughAlpha1Etc2, 8)]
    [InlineData(InternalFormat.CompressedSrgb8PunchthroughAlpha1Etc2, TextureFormat.CompressedSrgb8PunchthroughAlpha1Etc2, 8)]
    [InlineData(InternalFormat.CompressedRgba8Etc2Eac, TextureFormat.CompressedRgba8Etc2Eac, 16)]
    [InlineData(InternalFormat.CompressedSrgb8Alpha8Etc2Eac, TextureFormat.CompressedSrgb8Alpha8Etc2Eac, 16)]
    public void CoreCompressedFormatsPublishSuppliedFacts(InternalFormat nativeFormat, TextureFormat format, int bytes)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 2, 2, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            using var reads = new ImageDescriptorCapture();
            GL.CompressedTexImage2D(TextureTarget.Texture2D, 0, nativeFormat, 4, 4, 0, bytes, new byte[bytes]);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, texture));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Assert.True(entry.IsUsable);
            Assert.Equal(format, Assert.Single(entry.Metadata.Levels).Format);
            Assert.Equal(4, entry.Metadata.Levels[0].Width);
            Assert.Equal(0, reads.Reads);
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    /// <summary>Native views retain cumulative ranges and source identities through ordinary borrowed wrappers.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeViewsKeepOriginalStorageWithoutDiscovery(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var reads = new ImageDescriptorCapture();
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        uint source = 0, view = 0;
        ((delegate* unmanaged[Stdcall]<int, int, uint*, void>)table.Read("glCreateTextures"))((int)TextureTarget.Texture2DArray, 1, &source);
        ((delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, void>)table.Read("glTextureStorage3D"))(source, 4, (int)SizedInternalFormat.Rgba8, 8, 8, 4);
        ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glGenTextures"))(1, &view);
        try
        {
            ((delegate* unmanaged[Stdcall]<uint, int, uint, int, uint, uint, uint, uint, void>)table.Read("glTextureView"))(
                view, (int)TextureTarget.Texture2DArray, source, (int)PixelInternalFormat.Rgba8, 1, uint.MaxValue, 1, uint.MaxValue);
            using var borrowed = new BorrowedTexture((int)view);
            GpuTextureRef reference = borrowed;
            Assert.True(reference.TryGetMetadata(out var metadata));
            Assert.Equal(3, metadata.View!.Value.LevelCount); Assert.Equal(3, metadata.View.Value.LayerCount);
            Assert.Equal(4, metadata.Levels[0].Width); Assert.Equal(3, metadata.Levels[0].Depth);
            Assert.Equal((int)source, GpuResourceManager.TextureStorageName(borrowed));
            using var image = GpuFramebufferAttachment.FromTexture(borrowed, layer: 0);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            int notifications = 0;
            framebuffer.AttachmentsChanged += () => notifications++;
            GL.DeleteTexture((int)source); source = 0;
            Assert.False(reference.IsValid);
            Assert.Equal(0, image.Width);
            Assert.Equal(1, notifications);
            Assert.Equal(0, reads.Reads); Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture((int)view); if (source != 0) GL.DeleteTexture((int)source); fixture.CleanupGpuResources(); }
    }

    /// <summary>Temporary source withdrawal clears view projections and explicit metadata recovery restores them.</summary>
    [Fact]
    public void ViewImageTracksSourceMetadataAvailability()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var source = RHICommandContext.Current.CreateTexture(new RHITextureDescription {
                Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = 4, Height = 4,
                Depth = 2, MipLevels = 1, Storage = ResourceStorageMode.Immutable }).RequireResource();
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription {
                Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8,
                BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1 }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(view, layer: 0);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            int notifications = 0;
            framebuffer.AttachmentsChanged += () => notifications++;
            using (new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(source, GpuMetadataChange.Metadata)) { }
            Assert.False(view.IsValid);
            Assert.Equal(0, image.Width);
            Assert.Equal(1, notifications);
            GpuResourceManager.RecoverWrapperMetadata(source, new GpuTextureMetadataReader(TextureDimension.TwoArray));
            Assert.True(view.IsValid);
            Assert.Equal(4, image.Width);
            Assert.Equal(2, notifications);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Known read-framebuffer images establish copy definitions without descriptor scans.</summary>
    [Fact]
    public void CopyDefinitionUsesEstablishedReadSelection()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var source = RHICommandContext.Current.CreateTexture(new RHITextureDescription {
            Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = 4, Height = 4,
            MipLevels = 1, Storage = ResourceStorageMode.Immutable }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(source);
        using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
        int destination = GL.GenTexture();
        try
        {
            using var reads = new ImageDescriptorCapture();
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer.FboId);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            GL.BindTexture(TextureTarget.Texture2D, destination);
            GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 0, 0, 4, 4, 0);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, destination));
            Assert.True(entry.IsUsable); Assert.Equal(4, Assert.Single(entry.Metadata.Levels).Width);
            Assert.Equal(0, reads.Reads); Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture(destination); fixture.CleanupGpuResources(); }
    }

    /// <summary>A non-renderable secondary attachment prevents publication of a rejected copy definition.</summary>
    [Fact]
    public void CopyDefinitionRejectsIncompleteReadFramebuffer()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int source);
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int destination);
        GL.CreateFramebuffers(1, out int framebuffer);
        try
        {
            GL.TextureStorage2D(source, 1, SizedInternalFormat.Rgba8, 4, 4);
            GL.BindTexture(TextureTarget.Texture2D, destination);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 2, 2, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            GL.NamedFramebufferTexture(framebuffer, FramebufferAttachment.ColorAttachment0, source, 0);
            GL.NamedFramebufferTexture(framebuffer, FramebufferAttachment.DepthAttachment, source, 0);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            Assert.NotEqual(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.ReadFramebuffer));
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, destination));
            ulong revision = entry.MetadataRevision;
            GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 0, 0, 4, 4, 0);
            Assert.Equal(ErrorCode.InvalidFramebufferOperation, GL.GetError());
            Assert.True(entry.IsUsable);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(2, Assert.Single(entry.Metadata.Levels).Width);
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            GL.DeleteFramebuffer(framebuffer); GL.DeleteTexture(destination); GL.DeleteTexture(source);
            fixture.CleanupGpuResources();
        }
    }

    /// <summary>Layered color attachments with different targets cannot certify a copy from an incomplete framebuffer.</summary>
    [Fact]
    public void CopyDefinitionRejectsMixedLayeredColorTargets()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateTextures(TextureTarget.Texture2DArray, 1, out int array);
        GL.CreateTextures(TextureTarget.Texture3D, 1, out int volume);
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int destination);
        GL.CreateFramebuffers(1, out int framebuffer);
        try
        {
            GL.TextureStorage3D(array, 1, SizedInternalFormat.Rgba8, 4, 4, 2);
            GL.TextureStorage3D(volume, 1, SizedInternalFormat.Rgba8, 4, 4, 2);
            GL.BindTexture(TextureTarget.Texture2D, destination);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 2, 2, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            GL.NamedFramebufferTexture(framebuffer, FramebufferAttachment.ColorAttachment0, array, 0);
            GL.NamedFramebufferTexture(framebuffer, FramebufferAttachment.ColorAttachment1, volume, 0);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            Assert.NotEqual(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.ReadFramebuffer));
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, destination));
            ulong revision = entry.MetadataRevision;
            GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 0, 0, 4, 4, 0);
            Assert.Equal(ErrorCode.InvalidFramebufferOperation, GL.GetError());
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(2, Assert.Single(entry.Metadata.Levels).Width);
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            GL.DeleteFramebuffer(framebuffer); GL.DeleteTexture(destination); GL.DeleteTexture(volume); GL.DeleteTexture(array);
            fixture.CleanupGpuResources();
        }
    }

    /// <summary>Both binding roots retain reservation identity and derive immutable array geometry.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TextureReservationAndImmutableImagesAreMaintained(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var reads = new ImageDescriptorCapture();
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        var manager = GpuResourceManagerSystem.CurrentManager!;
        uint id = 0;
        ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glGenTextures"))(1, &id);
        try
        {
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.FindResource(GpuResourceKind.Texture, (int)id));
            Assert.False(entry.IsRealized); Assert.False(entry.IsUsable);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, (int)id, new ForbiddenReader()));
            ((delegate* unmanaged[Stdcall]<int, uint, void>)table.Read("glBindTexture"))((int)TextureTarget.Texture2DArray, id);
            Assert.True(entry.IsRealized); Assert.Empty(entry.Metadata.Levels);
            Assert.Equal(1000, entry.Metadata.MaxLevel);
            ((delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, void>)table.Read("glTextureStorage3D"))(id, 4, (int)SizedInternalFormat.Rgba8, 8, 4, 3);
            Assert.True(entry.IsUsable); Assert.Equal(4, entry.Metadata.Levels.Length);
            Assert.Equal(1, entry.Metadata.Levels[3].Width); Assert.Equal(1, entry.Metadata.Levels[3].Height);
            Assert.All(entry.Metadata.Levels, image => Assert.Equal(3, image.Depth));
            ulong revision = entry.StorageRevision;
            ((delegate* unmanaged[Stdcall]<uint, void>)table.Read("glGenerateTextureMipmap"))(id);
            Assert.Equal(revision, entry.StorageRevision);
            Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Texture, (int)id, new ForbiddenReader()));
            Assert.Equal(0, reads.Reads); Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture((int)id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Mutable definitions derive generated descendants while content-only regeneration preserves revisions.</summary>
    [Fact]
    public void MutableMipGenerationUsesKnownBaseImage()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var reads = new ImageDescriptorCapture();
        int id = GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, id);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, id));
            Assert.Single(entry.Metadata.Levels);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            Assert.Equal(4, entry.Metadata.Levels.Length);
            ulong revision = entry.StorageRevision;
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            Assert.Equal(revision, entry.StorageRevision);
            GL.TextureParameter(id, TextureParameterName.TextureMaxLevel, -1);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.Equal(1000, entry.Metadata.MaxLevel); Assert.Equal(revision, entry.StorageRevision);
            Assert.Equal(0, reads.Reads);
        }
        finally { GL.DeleteTexture(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Provably rejected cube generation preserves an existing operation's execution certificate.</summary>
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void RejectedCubeGenerationPreservesOperation(bool coreRoot, bool mismatchedFaces)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var reads = new ImageDescriptorCapture();
        int texture = GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.TextureCubeMap, texture);
            for (int face = 0; face < (mismatchedFaces ? 6 : 1); face++)
            {
                int size = face == 5 ? 4 : 8;
                GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + face, 0, PixelInternalFormat.Rgba8,
                    size, size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            }
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, texture));
            ulong revision = entry.MetadataRevision;
            using var operation = OpenGlStateCache.Current.EnterResourceOperation(new(GpuResourceKind.Texture, texture));
            var table = OpenGLBindingTable.For(coreRoot ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
            ((delegate* unmanaged[Stdcall]<uint, void>)table.Read("glGenerateTextureMipmap"))((uint)texture);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.False(operation.Cancelled);
            Assert.True(entry.IsUsable);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(0, reads.Reads);
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    /// <summary>An undefined mutable base leaves both descriptors and an existing operation unchanged.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UndefinedBaseGenerationPreservesOperation(bool coreRoot)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var reads = new ImageDescriptorCapture();
        int texture = GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            using var operation = OpenGlStateCache.Current.EnterResourceOperation(new(GpuResourceKind.Texture, texture));
            var table = OpenGLBindingTable.For(coreRoot ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
            ((delegate* unmanaged[Stdcall]<uint, void>)table.Read("glGenerateTextureMipmap"))((uint)texture);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Assert.False(operation.Cancelled);
            Assert.Equal(0, reads.Reads);
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    /// <summary>Native renderbuffer and framebuffer setup maintains supplied attachment/default descriptors.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FramebufferCompositionUsesMaintainedRenderbuffer(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var reads = new ImageDescriptorCapture();
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        uint rb = 0, fb = 0;
        ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glCreateRenderbuffers"))(1, &rb);
        ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glCreateFramebuffers"))(1, &fb);
        try
        {
            ((delegate* unmanaged[Stdcall]<uint, int, int, int, void>)table.Read("glNamedRenderbufferStorage"))(rb, (int)RenderbufferStorage.Rgba8, 7, 5);
            ((delegate* unmanaged[Stdcall]<uint, int, int, uint, void>)table.Read("glNamedFramebufferRenderbuffer"))(fb, (int)FramebufferAttachment.ColorAttachment0, (int)RenderbufferTarget.Renderbuffer, rb);
            ((delegate* unmanaged[Stdcall]<uint, int, int, void>)table.Read("glNamedFramebufferParameteri"))(fb, (int)FramebufferDefaultParameter.FramebufferDefaultWidth, 11);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var renderbuffer = Assert.IsType<GpuResourceMetadataEntry<GpuRenderbufferMetadata>>(manager.FindResource(GpuResourceKind.Renderbuffer, (int)rb));
            var framebuffer = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(manager.FindResource(GpuResourceKind.Framebuffer, (int)fb));
            Assert.Equal(new GpuRenderbufferMetadata(7, 5, TextureFormat.Rgba8, 0), renderbuffer.Metadata);
            Assert.Equal((nint)(int)rb, framebuffer.Metadata.Attachments[FramebufferSlot.Color0].Id);
            Assert.Equal(11, framebuffer.Metadata.DefaultWidth); Assert.True(framebuffer.Metadata.DefaultFixedSampleLocations);
            Assert.Equal(0, reads.Reads); Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteFramebuffer((int)fb); GL.DeleteRenderbuffer((int)rb); fixture.CleanupGpuResources(); }
    }
    #endregion

    #region Private
    /// <summary>Rejects descriptor discovery so canonical reuse cannot silently query native storage.</summary>
    private sealed class ForbiddenReader : IGpuResourceMetadataReader<GpuTextureMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public bool IsResource(nint id) => throw new InvalidOperationException("Unexpected image identity probe.");
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuTextureMetadata metadata) => throw new InvalidOperationException("Unexpected image discovery.");
        #endregion
    }
    #endregion

    #region Private
    /// <summary>Counts the one authorized pre-install texture discovery.</summary>
    private sealed class PreinstallTextureReader : IGpuResourceMetadataReader<GpuTextureMetadata>
    {
        private readonly GpuTextureMetadataReader reader = new(TextureDimension.Two);
        internal int Reads { get; private set; }
        /// <inheritdoc />
        public bool IsResource(nint id) => reader.IsResource(id);
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuTextureMetadata metadata)
        {
            Reads++;
            return reader.TryRead(id, out metadata);
        }
    }
    #endregion
}
