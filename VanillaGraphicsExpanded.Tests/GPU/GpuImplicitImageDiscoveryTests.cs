using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies explicit discovery of unknown EXT objects without callback readback or fabricated defaults.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed unsafe class GpuImplicitImageDiscoveryTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Unknown new and pre-existing textures defer discovery and then reuse their canonical facts.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UnknownTextureDiscoversOnceOnAdoption(bool coreRoot, bool existingStorage)
    {
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
            SetParameter(coreRoot, texture, TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 7);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.FindResource(GpuResourceKind.Texture, texture));
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
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var driver = new ImplicitImageDriver();
        int texture = GL.GenTexture();
        try
        {
            SetParameter(coreRoot, texture, TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 7);
            var entry = RHIResourceManagerSystem.CurrentManager!.AcquireResource(GpuResourceKind.Texture, texture, GpuTextureMetadataReader.Instance);
            Assert.NotNull(entry);
            Assert.True(entry.IsUsable);
            Assert.Equal(7, entry.Metadata.MaxLevel);
            Assert.Equal(GpuInitialDiscovery.None, entry.InitialDiscovery);
            Assert.Equal(0, driver.Reads);
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }

    /// <summary>A rejected target cannot become the purported target of an older unknown texture.</summary>
    [Fact]
    public void DiscoveryIdentifiesActualTargetAfterRejectedExtCommand()
    {
        PrepareUnobserved();
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        GL.TextureStorage2D(texture, 1, SizedInternalFormat.Rgba8, 8, 4);
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        using var driver = new ImplicitImageDriver();
        try
        {
            SetParameter(false, texture, TextureTarget.Texture3D, TextureParameterName.TextureMaxLevel, 7);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.Equal(0, driver.Reads);
            var entry = manager.AcquireResource(GpuResourceKind.Texture, texture, new GpuTextureMetadataReader(TextureDimension.Three));
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
        PrepareUnobserved();
        int texture = GL.GenTexture();
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        using var driver = new ImplicitImageDriver();
        try
        {
            SetParameter(false, texture, TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 7);
            var reader = new UnavailableReader();
            Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, texture, reader));
            SetParameter(false, texture, TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 5);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, texture, reader));
            Assert.Equal(1, reader.Reads);
            var entry = manager.RecoverResource(GpuResourceKind.Texture, texture, new GpuTextureMetadataReader(TextureDimension.Three));
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
        PrepareUnobserved();
        int framebuffer = GL.GenFramebuffer();
        int renderbuffer = GL.GenRenderbuffer();
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        using var driver = new ImplicitImageDriver();
        try
        {
            var table = Table(coreRoot);
            var storage = (delegate* unmanaged[Stdcall]<uint, int, int, int, void>)table.Read("glNamedRenderbufferStorageEXT");
            storage((uint)renderbuffer, (int)RenderbufferStorage.Rgba8, 8, 4);
            var status = (delegate* unmanaged[Stdcall]<uint, int, int>)table.Read("glCheckNamedFramebufferStatusEXT");
            Assert.Equal((int)FramebufferErrorCode.FramebufferIncompleteMissingAttachment, status((uint)framebuffer, (int)FramebufferTarget.Framebuffer));
            Assert.Equal(0, driver.Reads);
            Assert.False(manager.FindResource(GpuResourceKind.Framebuffer, framebuffer)!.IsUsable);
            Assert.False(manager.FindResource(GpuResourceKind.Renderbuffer, renderbuffer)!.IsUsable);
            Assert.False(OpenGlStateCache.Current.TryGetFramebufferReadRouting(framebuffer, out _));
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

    /// <summary>A later association with an untracked backing cannot leave an earlier supplied candidate usable.</summary>
    [Fact]
    public void UntrackedBackingDiscardsEarlierBufferCandidate()
    {
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
            var call = (delegate* unmanaged[Stdcall]<uint, int, int, uint, void>)Table(false).Read("glTextureBufferEXT");
            call((uint)texture, (int)TextureTarget.TextureBuffer, (int)SizedInternalFormat.R32f, (uint)tracked);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.FindResource(GpuResourceKind.Texture, texture));
            Assert.Equal((nint)tracked, entry.Metadata.Buffer!.Value.BufferId);
            call((uint)texture, (int)TextureTarget.TextureBuffer, (int)SizedInternalFormat.R32f, (uint)untracked);
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
            call((uint)texture, (int)TextureTarget.TextureBuffer, (int)SizedInternalFormat.R32f, (uint)buffer);
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
            var call = (delegate* unmanaged[Stdcall]<uint, int, int, uint, void>)Table(false).Read("glTextureBufferEXT");
            call((uint)texture, (int)TextureTarget.TextureBuffer, (int)SizedInternalFormat.R32f, (uint)buffer);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.Equal(0, driver.Reads);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Texture, texture, GpuTextureMetadataReader.Instance));
            Assert.False(manager.FindResource(GpuResourceKind.Texture, texture)!.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture(texture); GL.DeleteBuffer(buffer); fixture.CleanupGpuResources(); }
    }
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

        /// <inheritdoc />
        public bool IsResource(nint id) => GL.IsTexture((int)id);

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
