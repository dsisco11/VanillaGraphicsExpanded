using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Exercises shared wrapper ownership, alias guards and original-registration deletion safety.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuResourceWrapperLifecycleTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Ownership and alias guards
    /// <summary>Texture borrowers share externally updated facts without descriptor discovery.</summary>
    [Fact]
    public void ExternalTextureReallocationRefreshesSharedAliasesWithoutDescriptorReads()
    {
        // Arrange: Create shared texture wrappers and a descriptor-reader counter before external reallocation.
        using var lifetime = StartManager();
        using var reads = new ImageDescriptorCapture();
        using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var first = new BorrowedTexture(owner.TextureId);

        // Act: reallocate the underlying texture outside its wrapper, then acquire another borrower.
        using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, owner.TextureId))
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, 12, 6, 0,
                PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
        using var second = new BorrowedTexture(owner.TextureId);

        // Assert: both aliases receive the owner publication without a descriptor read.
        Assert.Equal(12, first.Width);
        Assert.Equal(6, first.Height);
        Assert.Equal(TextureFormat.Rgba16f, first.InternalFormat);
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(second.Width, owner.Width);
        Assert.Same(lifetime.Manager.GetResourceRegistration(first), lifetime.Manager.GetResourceRegistration(second));
        Assert.Equal(0, reads.Reads);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Renderbuffer borrowers share externally updated storage without descriptor discovery.</summary>
    [Fact]
    public void ExternalRenderbufferReallocationRefreshesSharedAliasesWithoutDescriptorReads()
    {
        // Arrange: Create shared renderbuffer wrappers and a descriptor-reader counter before external reallocation.
        using var lifetime = StartManager();
        using var reads = new ImageDescriptorCapture();
        using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 8, Height = 4, Samples = 0, Label = null }).RequireResource();
        using var first = OpenGLRenderbufferIntegration.Borrow(owner.RenderbufferId);

        // Act: update storage through the native renderbuffer call and acquire an alias afterward.
        using (OpenGlStateCache.Current.BindRenderbufferScope(owner.RenderbufferId))
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba16f, 12, 6);
        using var second = OpenGLRenderbufferIntegration.Borrow(owner.RenderbufferId);

        // Assert: aliases share updated facts without a second discovery query.
        Assert.Equal(12, first.Width);
        Assert.Equal(6, first.Height);
        Assert.Equal(TextureFormat.Rgba16f, first.Storage);
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(second.Width, owner.Width);
        Assert.Same(lifetime.Manager.GetResourceRegistration(first), lifetime.Manager.GetResourceRegistration(second));
        Assert.Equal(0, reads.Reads);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Replacing an attached owner preserves its old identity until the new image is attached.</summary>
    [Fact]
    public void ResizedOwnerDefersFramebufferDescriptorUntilBind()
    {
        // Arrange: retain the committed attachment descriptor while the owning array image is resized.
        using var lifetime = StartManager();
        using var texture = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(texture, layer: 0);
        using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(
            lifetime.Manager.GetResourceRegistration(framebuffer));
        var original = entry.Metadata.Attachments[FramebufferSlot.Color0];
        // Act: resize the owner before the framebuffer's next explicit bind boundary.
        Assert.True(texture.Resize(8, 4, 2));

        // Assert: the old descriptor remains committed until the framebuffer is rebound.
        Assert.Equal(original, entry.Metadata.Attachments[FramebufferSlot.Color0]);
        Assert.True(entry.MetadataAvailable);
        Assert.Null(lifetime.Manager.ProjectFramebufferDescriptor(original));
        // Act: binding refreshes the framebuffer descriptor from native attachment state.
        framebuffer.Bind();

        // Assert: metadata now names the resized owner's native image.
        Assert.Equal(texture.TextureId, entry.Metadata.Attachments[FramebufferSlot.Color0].Id);
        Assert.True(entry.MetadataAvailable);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            FramebufferParameterName.FramebufferAttachmentObjectName, out int native);
        Assert.Equal(texture.TextureId, native);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Explicit owner refresh adopts rebuilt framebuffer identity without reviving old aliases.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalFramebufferRefreshReplacesRetiredRegistration(bool reuseId)
    {
        // Arrange: create an externally wrapped framebuffer and retain an alias to its original registration.
        using var lifetime = StartManager();
        int original = GL.GenFramebuffer();
        OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.Framebuffer, original);
        using var external = DynamicGpuFramebuffer.Wrap(original, width: 4, height: 4);
        using var oldAlias = DynamicGpuFramebuffer.Wrap(original, width: 4, height: 4);
        var old = lifetime.Manager.GetResourceRegistration(external)!;
        int replacement = original;
        try
        {
            // The same-name case isolates identity replacement from the driver's free-name policy.
            if (reuseId)
            {
                lifetime.Manager.ObserveDeletion(GpuResourceKind.Framebuffer, original);
                lifetime.Manager.ObserveImageCreation(GpuResourceKind.Framebuffer, original, true);
            }
            else
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, original));
                replacement = GL.GenFramebuffer();
                OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.Framebuffer, replacement);
            }
            // Act: refresh the owner after its native identity has been observed as replaced.
            external.RefreshWrappedFramebuffer(replacement, 4, 4);

            // Assert: only the refreshed wrapper adopts the new registration.
            Assert.True(external.IsValid);
            Assert.False(oldAlias.IsValid);
            Assert.NotSame(old, lifetime.Manager.GetResourceRegistration(external));
            Assert.Same(old, lifetime.Manager.GetResourceRegistration(oldAlias));
            Assert.True(lifetime.Manager.IsResourceMetadataAvailable(external));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, replacement)); }
    }

    /// <summary>Native one-dimensional array discovery preserves layer zero as one unit-height plane.</summary>
    [Fact]
    public void NativeOneDimensionalArrayLayerZeroDiscoveryUsesSinglePlane()
    {
        // Arrange: create a native 1D-array image and attach its layer zero to a framebuffer.
        using var lifetime = StartManager();
        int texture = GL.GenTexture();
        int framebuffer = GL.GenFramebuffer();
        try
        {
            OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture1DArray, 0, texture);
            GL.TexImage2D(TextureTarget.Texture1DArray, 0, PixelInternalFormat.Rgba8, 8, 3, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.Framebuffer, framebuffer);
            GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, texture, 0, 0);
            // Act: discover the selected image through the production framebuffer descriptor reader.
            using var external = DynamicGpuFramebuffer.Wrap(framebuffer, width: 8, height: 1);
            using var image = GpuFramebufferAttachmentDiscovery.Read(external, FramebufferSlot.Color0);

            // Assert: one array layer projects to one unit-height plane.
            Assert.Equal(0, image.Layer);
            Assert.Equal(8, image.Width);
            Assert.Equal(1, image.Height);
            Assert.Equal(1, image.ImageDepth);
            Assert.True(lifetime.Manager.IsResourceMetadataAvailable(image));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, framebuffer));
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, texture));
        }
    }

    /// <summary>An allocated higher mip cannot masquerade as the missing base image of a borrowed texture.</summary>
    [Fact]
    public void BorrowedTextureRejectsSparseStorageWithoutLevelZero()
    {
        // Arrange: allocate only mip level one so the texture has no base image.
        using var lifetime = StartManager();
        int texture = GL.GenTexture();
        try
        {
            OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture2D, 0, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 1, PixelInternalFormat.Rgba8, 4, 2, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            // Act and assert: borrower construction rejects the sparse texture without retiring it.
            Assert.Throws<ArgumentException>(() => new BorrowedTexture(texture));
            Assert.True(GL.IsTexture(texture));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, texture)); }
    }

    /// <summary>Disposing one borrower removes its subscription without deleting or unbinding shared storage.</summary>
    [Fact]
    public void BorrowerDisposalPreservesOwnerAliasesAndBindings()
    {
        // Arrange: make two borrowers, preserve the owner, and bind its texture on a non-default unit.
        using var lifetime = StartManager();
        using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var borrower = new BorrowedTexture(owner.TextureId);
        using var surviving = new BorrowedTexture(owner.TextureId);
        var entry = lifetime.Manager.GetResourceRegistration(owner)!;
        Assert.Same(entry, lifetime.Manager.GetResourceRegistration(borrower));
        OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture2D, 2, owner.TextureId);
        // Act: dispose one borrower repeatedly.
        borrower.Dispose();
        borrower.Dispose();

        // Assert: the owner, surviving borrower, and native binding remain intact.
        Assert.True(GL.IsTexture(owner.TextureId));
        Assert.True(owner.IsValid);
        Assert.True(surviving.IsValid);
        Assert.False(lifetime.Manager.IsResourceMetadataTracked(borrower));
        Assert.True(lifetime.Manager.IsResourceMetadataAvailable(surviving));
        Assert.Equal(owner.TextureId, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 2));
        GL.ActiveTexture(TextureUnit.Texture2);
        Assert.Equal(owner.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
        Assert.Throws<NotSupportedException>(() => surviving.Detach());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Non-owning aliases follow metadata publication and withdraw on original owner retirement.</summary>
    [Fact]
    public void AliasReferencesDoNotBlockOwnerLifecycle()
    {
        // Arrange: retain a borrower while the owner still controls the shared texture.
        using var lifetime = StartManager();
        using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var borrower = new BorrowedTexture(owner.TextureId);
        int original = owner.TextureId;
        // Act: resize and then retire the owner while its alias remains alive.
        Assert.True(owner.Resize(16, 8));
        // Assert: the alias receives resized facts, then withdraws when native ownership ends.
        Assert.True(borrower.IsValid);
        Assert.Equal(16, borrower.Width);
        owner.Dispose();
        Assert.False(borrower.IsValid);
        Assert.False(GL.IsTexture(original));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Transferring ownership preserves the canonical entry and the remaining borrower's native object.</summary>
    [Fact]
    public void DetachPreservesLiveRegistrationAndBorrowers()
    {
        // Arrange: retain the canonical registration and a borrower before transferring ownership.
        using var lifetime = StartManager();
        using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var borrower = new BorrowedTexture(owner.TextureId);
        var original = lifetime.Manager.GetResourceRegistration(owner)!;
        // Act: detach the native name and keep it alive until explicit retirement.
        int transferred = (int)owner.Detach();
        try
        {
            // Assert: the wrapper is retired while the canonical entry and borrower remain live.
            Assert.True(owner.IsDisposed);
            Assert.Equal(0, owner.ResourceId);
            Assert.Same(original, lifetime.Manager.FindResource(GpuResourceKind.Texture, transferred));
            Assert.True(borrower.IsValid);
            Assert.True(lifetime.Manager.IsResourceMetadataAvailable(borrower));
            owner.Dispose();
            Assert.True(GL.IsTexture(transferred));
        }
        finally { RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, transferred)); }
        Assert.False(borrower.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion

    #region Native retirement and context isolation
    /// <summary>Closing a live manager drains owned attachment storage already queued for render-thread disposal.</summary>
    [Fact]
    public void ManagerDisposalDeletesQueuedOwnedAttachmentStorage()
    {
        // Arrange: queue attachment storage by disposing its wrapper before manager shutdown.
        using var lifetime = StartManager();
        using var attachment = new GpuFramebufferAttachment(8, 4, TextureFormat.Rgba8);
        int texture = attachment.TextureId;
        // Act: ordinary attachment disposal defers deletion until its manager drains.
        attachment.Dispose();
        Assert.True(GL.IsTexture(texture));

        // Assert: closing the manager drains the queued native owner.
        lifetime.Manager.Dispose();
        Assert.False(GL.IsTexture(texture));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Retired aliases cannot bind or reallocate a native name after its registry identity is replaced.</summary>
    [Fact]
    public void RetiredWrappersRejectOwnerAndTypedUseAfterNameReuse()
    {
        // Arrange: create real sentinel objects before replacing only the manager registrations for three names.
        using var lifetime = StartManager();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var textureSentinel = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        buffer.Allocate(32);
        using var bufferSentinel = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        bufferSentinel.Allocate(16);
        using var framebuffer = new DynamicGpuFramebuffer(true);
        framebuffer.Bind();
        using var framebufferSentinel = new DynamicGpuFramebuffer(true);
        framebufferSentinel.Bind();
        var manager = lifetime.Manager;

        // Keep real native objects as sentinels while deterministically simulating the observed
        // delete/recreate identity transition, independently of driver name recycling policy.
        // Act: model same-name recreation while retaining the original native objects.
        manager.ObserveDeletion(GpuResourceKind.Texture, texture.TextureId);
        var replacementTexture = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, texture.TextureId);
        manager.ObserveDeletion(GpuResourceKind.Buffer, buffer.BufferId);
        var replacementBuffer = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, buffer.BufferId);
        manager.ObserveDeletion(GpuResourceKind.Framebuffer, framebuffer.FboId);
        var replacementFramebuffer = manager.RegisterCreatedResource<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer, framebuffer.FboId);
        try
        {
            // Assert: typed operations reject stale wrappers while selector scopes and sentinels remain usable.
            RHICommandContext.Current.BindSampledTextures([new(2, textureSentinel, Dimension: TextureDimension.Two)]);
            bufferSentinel.Bind();
            Assert.Throws<InvalidOperationException>(() => ((GpuTextureRef)texture).Target);
            // Raw selector scopes operate on the current numeric identity, not the retired wrapper association.
            // They must still restore sentinels; typed operations below reject the old retained owner.
            using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 2, texture.TextureId))
                Assert.Equal(texture.TextureId, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 2));
            using (OpenGlStateCache.Current.BindBufferScope(BufferTarget.ShaderStorageBuffer, buffer.BufferId))
                Assert.Equal(buffer.BufferId, GL.GetInteger(GetPName.ShaderStorageBufferBinding));
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindBuffer(BufferRole.ShaderStorage, buffer));
#if DEBUG
            Assert.Throws<ObjectDisposedException>(() => texture.Resize(16, 8));
#else
            Assert.False(texture.Resize(16, 8));
#endif
            buffer.Allocate(64);
            Assert.Throws<ObjectDisposedException>(() => framebuffer.Bind());
#if DEBUG
            Assert.Throws<ObjectDisposedException>(() => framebuffer.Resize(16, 8));
#else
            Assert.False(framebuffer.Resize(16, 8));
#endif
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindSampledTextures(
                [new(2, texture, Dimension: TextureDimension.Two)]));
            buffer.Bind();
            Assert.Equal(textureSentinel.TextureId, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 2));
            Assert.Equal(bufferSentinel.BufferId, GL.GetInteger(GetPName.ShaderStorageBufferBinding));
            Assert.Equal(framebufferSentinel.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            GL.GetTextureLevelParameter(texture.TextureId, 0, GetTextureParameter.TextureWidth, out int width);
            Assert.Equal(8, width);
            GL.GetNamedBufferParameter(buffer.BufferId, BufferParameterName.BufferSize, out int size);
            Assert.Equal(32, size);
            Assert.Same(replacementTexture, manager.FindResource(GpuResourceKind.Texture, texture.TextureId));
            Assert.Same(replacementBuffer, manager.FindResource(GpuResourceKind.Buffer, buffer.BufferId));
            Assert.Same(replacementFramebuffer, manager.FindResource(GpuResourceKind.Framebuffer, framebuffer.FboId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, texture.TextureId));
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, buffer.BufferId));
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, framebuffer.FboId));
        }
    }

    /// <summary>Actual transpiled GL deletion retires aliases and preserves unrelated native bindings.</summary>
    [Fact]
    public void RedirectedExternalDeletionRetiresAliasesBeforeLaterOwnerDisposal()
    {
        // Arrange: install real aliases and a distinct texture binding before invoking redirected native deletion.
        using var lifetime = StartManager();
        using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var borrower = new BorrowedTexture(owner.TextureId);
        using var sentinel = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        int deleted = owner.TextureId;
        OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture2D, 2, deleted);
        OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture2D, 3, sentinel.TextureId);
        var harmony = Patch(nameof(DeleteNativeTexture));
        try
        {
            // Act: invoke the redirected OpenGL deletion entry point.
            DeleteNativeTexture(deleted);

            // Assert: the original association and native object are retired before a replacement is registered.
            Assert.Null(lifetime.Manager.FindResource(GpuResourceKind.Texture, deleted));
            Assert.False(owner.IsValid);
            Assert.False(borrower.IsValid);
            Assert.False(GL.IsTexture(deleted));
            Assert.Equal(0, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 2));
            Assert.Equal(sentinel.TextureId, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 3));
            // Deterministic identity reuse isolates stale owner protection from the driver's free-name policy.
            var replacement = lifetime.Manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, deleted);
            owner.Dispose();
            Assert.Same(replacement, lifetime.Manager.FindResource(GpuResourceKind.Texture, deleted));
            Assert.Equal(GpuResourceRegistrationState.Live, replacement.State);
            Assert.True(GL.IsTexture(sentinel.TextureId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>Worker retirement preserves aliases until the owner context drains the complete lifecycle transition.</summary>
    [Fact]
    public void QueuedOwnerRetirementDeletesOnlyAfterOriginatingContextDrains()
    {
        // Arrange: retain an alias and registration while owner disposal runs without its GL context.
        using var lifetime = StartManager();
        using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var borrower = new BorrowedTexture(owner.TextureId);
        int native = owner.TextureId;
        var original = lifetime.Manager.GetResourceRegistration(owner)!;
        // Act: queue disposal on the worker, then drain through the originating context's frame callback.
        RunWithoutCurrentContext(owner.Dispose);
        // Assert: before draining, native ownership remains live; after draining, all aliases retire.
        Assert.Equal(GpuResourceRegistrationState.Live, original.State);
        Assert.True(borrower.IsValid);
        Assert.True(lifetime.Manager.IsResourceMetadataAvailable(borrower));
        Assert.True(GL.IsTexture(native));
        lifetime.Manager.OnRenderFrame(0, RHIResourceManager.Stage);
        Assert.False(GL.IsTexture(native));
        Assert.Null(lifetime.Manager.FindResource(GpuResourceKind.Texture, native));
        Assert.Equal(GpuResourceRegistrationState.Deleted, original.State);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Queued owner work cannot delete the replacement registration for the same numeric native name.</summary>
    [Fact]
    public void QueuedRetirementCannotDeleteReplacementRegistration()
    {
        // Arrange: retain a native texture as a sentinel while queueing its original owner for disposal.
        using var lifetime = StartManager();
        using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var borrower = new BorrowedTexture(owner.TextureId);
        int native = owner.TextureId;
        var original = lifetime.Manager.GetResourceRegistration(owner)!;
        RunWithoutCurrentContext(owner.Dispose);
        Assert.Equal(GpuResourceRegistrationState.Live, original.State);
        Assert.True(borrower.IsValid);
        Assert.True(GL.IsTexture(native));
        try
        {
            // Act: simulate the observed-delete/recreate registry transition while preserving a real sentinel
            // object under the same name. Stale queued deletion must leave that object untouched.
            lifetime.Manager.ObserveDeletion(GpuResourceKind.Texture, native);
            var replacement = lifetime.Manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, native);
            lifetime.Manager.OnRenderFrame(0, RHIResourceManager.Stage);
            // Assert: draining stale retirement work preserves the replacement registration and native object.
            Assert.True(GL.IsTexture(native));
            Assert.Same(replacement, lifetime.Manager.FindResource(GpuResourceKind.Texture, native));
            Assert.False(borrower.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, native)); }
    }

    /// <summary>Retiring an originating context discards pending native work rather than deleting in its replacement.</summary>
    [Fact]
    public void ContextRetirementDiscardsQueuedDeletionInReplacementContextLifetime()
    {
        // Arrange: queue native deletion, then retain the same physical object while the origin context is replaced.
        using var lifetime = StartManager();
        using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var borrower = new BorrowedTexture(owner.TextureId);
        int native = owner.TextureId;
        object replacementContext = new();
        RunWithoutCurrentContext(owner.Dispose);
        try
        {
            // Act: retire the origin and attempt to drain pending work under the replacement lifetime.
            RenderContextRegistry.Retire(fixture);
            RenderContextRegistry.RegisterCurrent(replacementContext, static _ => true);
            lifetime.Manager.OnRenderFrame(0, RHIResourceManager.Stage);
            lifetime.Manager.Dispose();
            // Assert: managed aliases retire, but no native deletion is issued through the replacement context.
            Assert.False(borrower.IsValid);
            Assert.True(GL.IsTexture(native));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RenderContextRegistry.Retire(replacementContext);
            fixture.RestoreContextRegistration();
            GL.DeleteTexture(native);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion

    #region Native overload redirection
    /// <summary>Signed/unsigned arrays, references, pointers and single calls retire every affected texture alias.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RedirectedDeletionOverloadsRetireEveryListedResource(int variant)
    {
        // Arrange: create two owners, aliases, and a separate texture binding before invoking redirected deletion.
        using var lifetime = StartManager();
        using var first = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var second = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var firstAlias = new BorrowedTexture(first.TextureId);
        using var secondAlias = new BorrowedTexture(second.TextureId);
        using var sentinel = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture2D, 2, first.TextureId);
        OpenGLImageBindings.BindTexture(OpenGlStateCache.Current, TextureTarget.Texture2D, 3, sentinel.TextureId);
        var harmony = Patch(nameof(DeleteNativeTextureList));
        try
        {
            // Act: execute the selected native deletion overload through its redirected call site.
            DeleteNativeTextureList(variant, first.TextureId, second.TextureId);

            // Assert: both resources and aliases retire while the sentinel binding remains untouched.
            Assert.False(first.IsValid);
            Assert.False(second.IsValid);
            Assert.False(firstAlias.IsValid);
            Assert.False(secondAlias.IsValid);
            Assert.Null(lifetime.Manager.FindResource(GpuResourceKind.Texture, first.TextureId));
            Assert.Null(lifetime.Manager.FindResource(GpuResourceKind.Texture, second.TextureId));
            Assert.Equal(0, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 2));
            Assert.Equal(sentinel.TextureId, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 3));
            Assert.True(GL.IsTexture(sentinel.TextureId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>A rejected native list deletion leaves live registry state and the driver's error intact.</summary>
    [Fact]
    public void RejectedRedirectedDeletionPreservesResourceAndError()
    {
        // Arrange: retain an owner, alias, and canonical entry before invoking redirected invalid deletion.
        using var lifetime = StartManager();
        using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var alias = new BorrowedTexture(owner.TextureId);
        var original = lifetime.Manager.GetResourceRegistration(owner)!;
        var harmony = Patch(nameof(DeleteInvalidTextureList));
        try
        {
            // Act: issue a negative-count deletion request through the redirected native entry point.
            DeleteInvalidTextureList(owner.TextureId);

            // Assert: the driver error is preserved and the original object remains registered and usable.
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.True(GL.IsTexture(owner.TextureId));
            Assert.True(alias.IsValid);
            Assert.Same(original, lifetime.Manager.FindResource(GpuResourceKind.Texture, owner.TextureId));
            Assert.True(lifetime.Manager.IsResourceMetadataAvailable(alias));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>A nested EXT deletion call retires framebuffer aliases while preserving an unrelated draw binding.</summary>
    [Fact]
    public void RedirectedExtensionFramebufferDeletionRetiresAliases()
    {
        // Arrange: keep an unrelated draw framebuffer bound while selecting the owner for read deletion.
        using var lifetime = StartManager();
        using var owner = new DynamicGpuFramebuffer(true);
        owner.Bind();
        using var alias = new DynamicGpuFramebuffer(false, owner.FboId);
        using var sentinel = new DynamicGpuFramebuffer(true);
        sentinel.Bind();
        var cache = OpenGlStateCache.Current;
        OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.ReadFramebuffer, owner.FboId);
        var harmony = Patch(nameof(DeleteNativeExtensionFramebuffer));
        try
        {
            // Act: execute the nested EXT framebuffer deletion entry point.
            DeleteNativeExtensionFramebuffer(owner.FboId);

            // Assert: aliases retire while the unrelated draw target remains bound.
            Assert.False(owner.IsValid);
            Assert.False(alias.IsValid);
            Assert.Null(lifetime.Manager.FindResource(GpuResourceKind.Framebuffer, owner.FboId));
            Assert.Equal(0, cache.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            Assert.Equal(sentinel.FboId, cache.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer));
            Assert.True(GL.IsFramebuffer(sentinel.FboId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    #endregion
    #endregion

    #region Private
    #region Fixture initialization
    /// <summary>Starts an isolated manager while retaining the shared hidden native context.</summary>
    private ManagerLifetime StartManager()
    {
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        OpenGlStateCache.Current.InvalidateAll();
        return new ManagerLifetime();
    }
    #endregion

    #region Native redirection
    /// <summary>Provides an uninlined external call site for the production Harmony transpiler.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DeleteNativeTexture(int texture) => GL.DeleteTexture(texture);

    /// <summary>Creates a Harmony instance for the named external native call site.</summary>
    private static Harmony Patch(string method)
    {
        var harmony = new Harmony($"VGE.Tests.GpuResourceWrapperLifecycle.{method}");

        return harmony;
    }

    /// <summary>Exercises distinct managed overload shapes without bypassing native redirection.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void DeleteNativeTextureList(int variant, int first, int second)
    {
        uint[] unsigned = [unchecked((uint)first), unchecked((uint)second)];
        switch (variant)
        {
            case 0: GL.DeleteTextures(2, new[] { first, second }); break;
            case 1: GL.DeleteTextures(2, unsigned); break;
            case 2: GL.DeleteTextures(2, ref unsigned[0]); break;
            case 3:
                fixed (uint* pointer = unsigned) GL.DeleteTextures(2, pointer);
                break;
            default: GL.DeleteTexture(unchecked((uint)first)); GL.DeleteTexture(unchecked((uint)second)); break;
        }
    }

    /// <summary>Supplies an invalid count so the driver rejects deletion without changing live identity.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DeleteInvalidTextureList(int texture) => GL.DeleteTextures(-1, new[] { texture });

    /// <summary>Exercises a nested extension declaring type through the same call-map transpiler.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DeleteNativeExtensionFramebuffer(int framebuffer) => GL.Ext.DeleteFramebuffers(1, ref framebuffer);
    #endregion

    #region Worker and manager lifetimes
    /// <summary>Runs retirement on a context-free thread while keeping the test's native context on its original thread.</summary>
    private static void RunWithoutCurrentContext(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var worker = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        });
        worker.Start();
        worker.Join();
        failure?.Throw();
    }

    /// <summary>Scopes the global manager service and clears native cache knowledge after each test.</summary>
    private sealed class ManagerLifetime : IDisposable
    {
        #region Public API
        /// <summary>Drains native retirement in the original context and closes service admission.</summary>
        public void Dispose()
        {
            Manager.Dispose();
            RHIResourceManagerSystem.Shutdown();
            GL.ActiveTexture(TextureUnit.Texture0);
            OpenGlStateCache.Current.InvalidateAll();
        }
        #endregion

        #region Internal API
        /// <summary>Installs a new context-scoped manager before wrappers capture their origin.</summary>
        internal ManagerLifetime() { RHIResourceManagerSystem.Initialize(Manager); }

        /// <summary>Gets the manager whose registry and deferred work are under test.</summary>
        internal RHIResourceManager Manager { get; } = new();
        #endregion
    }
    #endregion
    #endregion
}
