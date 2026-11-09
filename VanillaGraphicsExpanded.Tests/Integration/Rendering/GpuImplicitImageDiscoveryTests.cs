using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Qualifies explicit discovery of unknown EXT objects without callback readback or fabricated defaults.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed unsafe class GpuImplicitImageDiscoveryTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Texture discovery and query avoidance
    /// <summary>Unknown new and pre-existing textures defer discovery and then reuse their canonical facts.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UnknownTextureDiscoversOnceOnAdoption(bool coreRoot, bool existingStorage)
    {
        // Arrange: Prepare a new or pre-existing texture and select the core/EXT binding root for discovery.
        PrepareUnobserved();
        int texture = GL.GenTexture();
        if (existingStorage)
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
        }
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        using var driver = new ImplicitImageDriver();
        try
        {
            // Act: mutate a newly observed name through EXT, then acquire its deferred snapshot.
            SetParameter(coreRoot, texture, TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 7);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.FindResource(GpuResourceKind.Texture, texture));
            // Assert: implicit creation records no fabricated facts or descriptor reads before adoption.
            Assert.False(entry.IsUsable);
            Assert.Equal(GpuInitialDiscovery.ImplicitCreation, entry.InitialDiscovery);
            Assert.Equal(0, driver.Reads);
            Assert.Equal(1, driver.Calls);
            var acquired = manager.AcquireResource(GpuResourceKind.Texture, texture, GpuTextureMetadataReader.Instance);
            Assert.Same(entry, acquired);
            Assert.True(entry.IsUsable);
            Assert.Equal(7, entry.Metadata.MaxLevel);
            if (existingStorage) Assert.Equal(8, Assert.Single(entry.Metadata.Levels).Width);
            else Assert.Empty(entry.Metadata.Levels);
            int initialReads = driver.Reads;
            Assert.True(initialReads > 0);
            SetParameter(coreRoot, texture, TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 5);
            Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Texture, texture, GpuTextureMetadataReader.Instance));
            Assert.Equal(5, entry.Metadata.MaxLevel);
            Assert.Equal(initialReads, driver.Reads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    /// <summary>A tracked generated reservation realizes without any metadata read.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrackedReservationRemainsQueryFree(bool coreRoot)
    {
        // Arrange: Reserve a known texture identity and install a metadata-reader counter before realization.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var driver = new ImplicitImageDriver();
        int texture = GL.GenTexture();
        try
        {
            // Act: mutate a manager-tracked reservation through either native binding root.
            SetParameter(coreRoot, texture, TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 7);
            var entry = RHIResourceManagerSystem.CurrentManager!.AcquireResource(GpuResourceKind.Texture, texture, GpuTextureMetadataReader.Instance);
            // Assert: tracked identity becomes usable from maintained facts without invoking the reader.
            Assert.NotNull(entry);
            Assert.True(entry.IsUsable);
            Assert.Equal(7, entry.Metadata.MaxLevel);
            Assert.Equal(GpuInitialDiscovery.None, entry.InitialDiscovery);
            Assert.Equal(0, driver.Reads);
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    #endregion

    #region Failed operations and deferred attachment discovery

    /// <summary>A rejected target cannot become the purported target of an older unknown texture.</summary>
    [Fact]
    public void DiscoveryIdentifiesActualTargetAfterRejectedExtCommand()
    {
        // Arrange: Create an unknown texture and prepare a rejected EXT operation naming a different target.
        PrepareUnobserved();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, 8, 4);
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        using var driver = new ImplicitImageDriver();
        try
        {
            // Act: submit an EXT update with the wrong target, then request the actual target explicitly.
            SetParameter(false, texture, TextureTarget.Texture3D, TextureParameterName.TextureMaxLevel, 7);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.Equal(0, driver.Reads);
            var entry = manager.AcquireResource(GpuResourceKind.Texture, texture, new GpuTextureMetadataReader(TextureDimension.Three));
            // Assert: the rejected command does not poison later discovery of the texture's true 2D target.
            Assert.NotNull(entry);
            Assert.Equal(TextureDimension.Two, entry.Metadata.Target);
            Assert.Equal(8, Assert.Single(entry.Metadata.Levels).Width);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    /// <summary>Failed initial discovery cannot be rearmed by repeated EXT calls or ordinary acquisition.</summary>
    [Fact]
    public void FailedDiscoveryRequiresExplicitRecovery()
    {
        // Arrange: Make initial metadata discovery fail for an unknown texture and retain its observation counter.
        PrepareUnobserved();
        int texture = GL.GenTexture();
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        using var driver = new ImplicitImageDriver();
        try
        {
            // Act: fail the first snapshot, repeat the EXT mutation, then request explicit recovery.
            SetParameter(false, texture, TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 7);
            var reader = new UnavailableReader();
            Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, texture, reader));
            SetParameter(false, texture, TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 5);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, texture, reader));
            Assert.Equal(1, reader.Reads);
            var entry = manager.RecoverResource(GpuResourceKind.Texture, texture, new GpuTextureMetadataReader(TextureDimension.Three));
            // Assert: repeated ordinary acquisition cannot rearm discovery; explicit recovery uses current native facts.
            Assert.NotNull(entry);
            Assert.True(entry.IsUsable);
            Assert.Equal(TextureDimension.Two, entry.Metadata.Target);
            Assert.Equal(5, entry.Metadata.MaxLevel);
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    /// <summary>Unknown EXT framebuffer and renderbuffer identities defer their complete descriptor reads.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FramebufferAndRenderbufferDiscoveryIsDeferred(bool coreRoot)
    {
        // Arrange: Create unknown framebuffer and renderbuffer identities outside tracked resource creation.
        PrepareUnobserved();
        int framebuffer = GL.GenFramebuffer();
        int renderbuffer = GL.GenRenderbuffer();
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        using var driver = new ImplicitImageDriver();
        try
        {
            // Act: publish native renderbuffer storage and query framebuffer status before adopting either name.
            var table = Table(coreRoot);
            var storage = (delegate* unmanaged[Stdcall]<uint, int, int, int, void>)table.Read("glNamedRenderbufferStorageEXT");
            storage((uint)renderbuffer, (int)RenderbufferStorage.Rgba8, 8, 4);
            var status = (delegate* unmanaged[Stdcall]<uint, int, int>)table.Read("glCheckNamedFramebufferStatusEXT");
            Assert.Equal((int)FramebufferErrorCode.FramebufferIncompleteMissingAttachment, status((uint)framebuffer, (int)FramebufferTarget.Framebuffer));
            Assert.Equal(0, driver.Reads);
            Assert.False(manager.FindResource(GpuResourceKind.Framebuffer, framebuffer)!.IsUsable);
            Assert.False(manager.FindResource(GpuResourceKind.Renderbuffer, renderbuffer)!.IsUsable);
            Assert.False(OpenGlStateCache.Current.TryGetFramebufferReadRouting(framebuffer, out _));
            // Assert: deferred descriptors are read once during adoption and reused by later requests.
            var rb = manager.AcquireResource(GpuResourceKind.Renderbuffer, renderbuffer, GpuRenderbufferMetadataReader.Instance);
            var fb = manager.AcquireResource(GpuResourceKind.Framebuffer, framebuffer, GpuFramebufferMetadataReader.Instance);
            Assert.Equal(8, rb!.Metadata.Width);
            Assert.Empty(fb!.Metadata.Attachments);
            int initialReads = driver.Reads;
            Assert.True(initialReads > 0);
            Assert.Same(rb, manager.AcquireResource(GpuResourceKind.Renderbuffer, renderbuffer, GpuRenderbufferMetadataReader.Instance));
            Assert.Same(fb, manager.AcquireResource(GpuResourceKind.Framebuffer, framebuffer, GpuFramebufferMetadataReader.Instance));
            Assert.Equal(initialReads, driver.Reads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteFramebuffer(framebuffer); GL.DeleteRenderbuffer(renderbuffer); fixture.CleanupGpuResources(); }
    }

    /// <summary>Buffer texture discovery verifies its target and preserves supplied mode, range and original backing.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BufferTextureDiscoveryPreservesObservedAssociation(bool coreRoot, bool whole)
    {
        // Arrange: Create texture and buffer storage with the selected core/EXT association and whole/ranged form.
        PrepareUnobserved();
        int texture = GL.GenTexture();
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        using var driver = new ImplicitImageDriver();
        GL.CreateBuffers(1, out int buffer);
        nint offset = GpuSupport.Graphics.TextureBufferOffsetAlignment;
        GL.NamedBufferData(buffer, offset + 64, nint.Zero, BufferUsageHint.StaticDraw);
        try
        {
            // Act: attach the tracked buffer through whole-buffer or ranged EXT texture operations.
            var table = Table(coreRoot);
            if (whole)
            {
                var call = (delegate* unmanaged[Stdcall]<uint, int, int, uint, void>)table.Read("glTextureBufferEXT");
                call((uint)texture, (int)TextureTarget.TextureBuffer, (int)SizedInternalFormat.R32f, (uint)buffer);
            }
            else
            {
                var call = (delegate* unmanaged[Stdcall]<uint, int, int, uint, nint, nint, void>)table.Read("glTextureBufferRangeEXT");
                call((uint)texture, (int)TextureTarget.TextureBuffer, (int)SizedInternalFormat.R32f, (uint)buffer, offset, 16);
            }
            var original = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.FindResource(GpuResourceKind.Texture, texture));
            Assert.False(original.IsUsable);
            Assert.Equal(0, driver.Reads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            var entry = manager.AcquireResource(GpuResourceKind.Texture, texture, GpuTextureMetadataReader.Instance);
            // Assert: the original entry publishes the exact association and later reuse performs no extra reads.
            Assert.Same(original, entry);
            Assert.True(entry!.IsUsable);
            Assert.Equal(TextureDimension.Buffer, entry.Metadata.Target);
            var association = entry.Metadata.Buffer!.Value;
            Assert.Equal((nint)buffer, association.BufferId);
            Assert.Equal(whole, association.WholeBuffer);
            Assert.Equal(whole ? (nint)0 : offset, association.Offset);
            Assert.Equal(whole ? (nint)0 : (nint)16, association.Size);
            int initialReads = driver.Reads;
            Assert.True(initialReads > 0);
            Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Texture, texture, GpuTextureMetadataReader.Instance));
            Assert.Equal(initialReads, driver.Reads);
            GL.DeleteBuffer(buffer); buffer = 0;
            Assert.False(entry.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture(texture); if (buffer != 0) GL.DeleteBuffer(buffer); fixture.CleanupGpuResources(); }
    }

    #endregion

    #region Association recovery and unavailable metadata

    /// <summary>A later association with an untracked backing cannot leave an earlier supplied candidate usable.</summary>
    [Fact]
    public void UntrackedBackingDiscardsEarlierBufferCandidate()
    {
        // Arrange: Associate a tracked buffer with a texture, then prepare an untracked replacement backing.
        PrepareUnobserved();
        int texture = GL.GenTexture();
        GL.CreateBuffers(1, out int untracked);
        GL.NamedBufferData(untracked, 64, nint.Zero, BufferUsageHint.StaticDraw);
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        using var driver = new ImplicitImageDriver();
        GL.CreateBuffers(1, out int tracked);
        GL.NamedBufferData(tracked, 64, nint.Zero, BufferUsageHint.StaticDraw);
        try
        {
            // Act: first submit a valid tracked backing, then replace it with an untracked backing.
            var call = (delegate* unmanaged[Stdcall]<uint, int, int, uint, void>)Table(false).Read("glTextureBufferEXT");
            call((uint)texture, (int)TextureTarget.TextureBuffer, (int)SizedInternalFormat.R32f, (uint)tracked);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.FindResource(GpuResourceKind.Texture, texture));
            Assert.Equal((nint)tracked, entry.Metadata.Buffer!.Value.BufferId);
            call((uint)texture, (int)TextureTarget.TextureBuffer, (int)SizedInternalFormat.R32f, (uint)untracked);
            // Assert: the latest untracked association withdraws the earlier candidate rather than preserving stale facts.
            Assert.Null(entry.Metadata.Buffer);
            Assert.False(entry.IsUsable);
            Assert.Equal(0, driver.Reads);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, texture, GpuTextureMetadataReader.Instance));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture(texture); GL.DeleteBuffer(tracked); GL.DeleteBuffer(untracked); fixture.CleanupGpuResources(); }
    }

    /// <summary>A rejected buffer association cannot mutate a tracked texture with a different target.</summary>
    [Fact]
    public void KnownNonBufferTargetRetainsMetadataAfterRejectedAssociation()
    {
        // Arrange: Track a known non-buffer texture target and prepare a buffer association the driver rejects.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var driver = new ImplicitImageDriver();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, 8, 4);
        GL.CreateBuffers(1, out int buffer);
        GL.NamedBufferData(buffer, 64, nint.Zero, BufferUsageHint.StaticDraw);
        try
        {
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, texture));
            var before = entry.Metadata;
            ulong revision = entry.MetadataRevision;
            var call = (delegate* unmanaged[Stdcall]<uint, int, int, uint, void>)Table(false).Read("glTextureBufferEXT");
            // Act: try to associate a buffer with a texture already known to have a 2D target.
            call((uint)texture, (int)TextureTarget.TextureBuffer, (int)SizedInternalFormat.R32f, (uint)buffer);
            // Assert: native rejection preserves the prior usable metadata and avoids discovery.
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.True(entry.IsUsable);
            Assert.Equal(before, entry.Metadata);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(0, driver.Reads);
        }
        finally { GL.DeleteTexture(texture); GL.DeleteBuffer(buffer); fixture.CleanupGpuResources(); }
    }

    /// <summary>A rejected EXT buffer request cannot certify an older unknown texture as a buffer texture.</summary>
    [Fact]
    public void UnknownNonBufferTargetRemainsUnavailableAfterRejectedAssociation()
    {
        // Arrange: Keep the texture target unknown and prepare a rejected EXT buffer-texture association.
        PrepareUnobserved();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, 8, 4);
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        using var driver = new ImplicitImageDriver();
        GL.CreateBuffers(1, out int buffer);
        GL.NamedBufferData(buffer, 64, nint.Zero, BufferUsageHint.StaticDraw);
        try
        {
            // Act: submit an invalid buffer association against an older unknown 2D texture.
            var call = (delegate* unmanaged[Stdcall]<uint, int, int, uint, void>)Table(false).Read("glTextureBufferEXT");
            call((uint)texture, (int)TextureTarget.TextureBuffer, (int)SizedInternalFormat.R32f, (uint)buffer);
            // Assert: rejection cannot falsely certify the name or permit ordinary fallback discovery.
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.Equal(0, driver.Reads);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, texture, GpuTextureMetadataReader.Instance));
            Assert.False(manager.FindResource(GpuResourceKind.Texture, texture)!.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture(texture); GL.DeleteBuffer(buffer); fixture.CleanupGpuResources(); }
    }
    #endregion

    #endregion

    #region Private
    /// <summary>Establishes real driver names before the manager and native observation are active.</summary>
    private void PrepareUnobserved()
    {
        fixture.EnsureContextValid();
        OpenGLBindingInterception.Uninstall();
        RHIResourceManagerSystem.CurrentManager?.Dispose();
        RHIResourceManagerSystem.Shutdown();
    }

    /// <summary>Selects the compiler-derived binding table for the requested OpenTK root.</summary>
    private static OpenGLBindingTable Table(bool coreRoot) => OpenGLBindingTable.For(coreRoot ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));

    /// <summary>Invokes the real generated callback using the EXT native parameter ABI.</summary>
    private static void SetParameter(bool coreRoot, int texture, TextureTarget target, TextureParameterName parameter, int value)
    {
        var call = (delegate* unmanaged[Stdcall]<uint, int, int, int, void>)Table(coreRoot).Read("glTextureParameteriEXT");
        call((uint)texture, (int)target, (int)parameter, value);
    }

    /// <summary>Counts a deliberately unavailable reader without manufacturing a successful snapshot.</summary>
    private sealed class UnavailableReader : IGpuResourceMetadataReader<GpuTextureMetadata>
    {
        internal int Reads;

        #region Public API

        /// <inheritdoc />
        public bool IsResource(nint id) => GL.IsTexture((int)id);

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
