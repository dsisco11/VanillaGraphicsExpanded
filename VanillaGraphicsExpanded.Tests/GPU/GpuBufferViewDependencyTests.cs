using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks buffer texture certificates against the original backing allocation and its observation failures.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuBufferViewDependencyTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Both allocation paths refresh aliases without losing the distinction between whole and explicit ranges.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BackingReallocationRefreshesViewAliases(bool native, bool ranged)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch();
        try
        {
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            using var view = CreateView(buffer, ranged);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = (GpuResourceMetadataEntry<GpuTextureMetadata>)manager.GetResourceRegistration(view)!;
            using var alias = new TextureAlias(view.TextureId);
            manager.AssociateResource(entry, alias);
            ulong revision = entry.MetadataRevision, storage = entry.StorageRevision;
            Resize(buffer, 128, native);
            Assert.True(manager.IsResourceMetadataAvailable(view));
            Assert.True(manager.IsResourceMetadataAvailable(alias));
            Assert.True(entry.MetadataRevision > revision);
            Assert.True(entry.StorageRevision > storage);
            Assert.Equal(buffer.BufferId, view.BufferId);
            Assert.Equal(ranged ? 64 : 0, view.SizeBytes);
            Assert.Equal(!ranged, alias.Snapshot.Buffer!.Value.WholeBuffer);
            Assert.Equal(ranged ? 64 : 0, alias.Snapshot.Buffer.Value.Size);
            Resize(buffer, 32, native);
            Assert.True(manager.IsResourceMetadataAvailable(view));
            Assert.Equal(ranged ? 64 : 0, view.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A failed backing read withdraws dependent aliases, and native recovery restores their certificates.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackingReadFailureWithdrawsViewsUntilRecovery(bool ranged)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch();
        try
        {
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            using var view = CreateView(buffer, ranged);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = (GpuResourceMetadataEntry<GpuBufferMetadata>)manager.GetResourceRegistration(buffer)!;
            using var alias = new TextureAlias(view.TextureId);
            manager.AssociateResource((GpuResourceMetadataEntry<GpuTextureMetadata>)manager.GetResourceRegistration(view)!, alias);
            using var sentinelBuffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
            sentinelBuffer.Allocate(64);
            using var sentinel = CreateView(sentinelBuffer, false);
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 0, sentinel.TextureId, 0, false, 0, TextureAccess.ReadOnly, (SizedInternalFormat)OpenGLImageValues.Format(((GpuTextureRef)sentinel).RequireMetadata().Buffer!.Value.Format));
            var original = entry.Reader ?? new GpuBufferMetadataReader();
            entry.Reader = new UnavailableReader();
            Assert.Throws<InvalidOperationException>(() => Resize(buffer, 128, true));
            Assert.False(manager.IsResourceMetadataAvailable(view));
            Assert.False(view.IsValid);
            Assert.False(manager.IsResourceMetadataAvailable(alias));
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindSampledTextures([new(0, view, Dimension: TextureDimension.Buffer)]));
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 0, out int image);
            Assert.Equal(sentinel.TextureId, image);
            entry.Reader = original;
            Assert.True(manager.RefreshMetadata(entry, GpuMetadataChange.None));
            Assert.True(manager.IsResourceMetadataAvailable(alias));
            Assert.True(manager.IsResourceMetadataAvailable(view));
            Assert.Equal(ranged ? 64 : 0, view.SizeBytes);
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 0, view.TextureId, 0, false, 0, TextureAccess.ReadOnly, (SizedInternalFormat)OpenGLImageValues.Format(((GpuTextureRef)view).RequireMetadata().Buffer!.Value.Format));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Every direct binding consumer rejects an explicitly unavailable view before changing a sentinel binding.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnavailableViewCannotBeBound(int consumer)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            using var view = CreateView(buffer, false);
            using var sentinel = CreateView(buffer, false);
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, sentinel, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)sentinel).Target)))]);
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 0, sentinel.TextureId, 0, false, 0, TextureAccess.ReadOnly, (SizedInternalFormat)OpenGLImageValues.Format(((GpuTextureRef)sentinel).RequireMetadata().Buffer!.Value.Format));
            GpuResourceManager.InvalidateWrapperMetadata(view);
            buffer.Allocate(128);
            Assert.False(view.IsValid);
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (consumer == 0) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, view, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)view).Target)))]);
                else if (consumer == 1) VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 0, view.TextureId, 0, false, 0, TextureAccess.ReadOnly, (SizedInternalFormat)OpenGLImageValues.Format(((GpuTextureRef)view).RequireMetadata().Buffer!.Value.Format));
                else { using var binding = new VanillaGraphicsExpanded.Tests.GPU.Fixtures.NativeImageSelection(0, view); }
            });
            OpenGLImageBindings.ActiveTexture(OpenGlStateCache.Current, 0);
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBindingBuffer));
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 0, out int image);
            Assert.Equal(sentinel.TextureId, image);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A new registration under a deleted backing name cannot recertify views of the retired allocation.</summary>
    [Fact]
    public void BackingDeletionAndReusedRegistrationDoNotReviveViews()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
            buffer.Allocate(64);
            using var view = CreateView(buffer, false);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            int oldId = buffer.BufferId;
            var original = (GpuResourceMetadataEntry<GpuBufferMetadata>)manager.GetResourceRegistration(buffer)!;
            var originalMetadata = original.Metadata;
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, oldId));
            Assert.False(manager.IsResourceMetadataAvailable(view));
            // Deterministically exercise registry name reuse without assuming the driver's allocator reuses this integer.
            var replacement = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, oldId);
            Assert.NotSame(original, replacement);
            manager.PublishMetadata(replacement, originalMetadata, GpuMetadataChange.Storage);
            Assert.True(replacement.IsUsable);
            var textureEntry = (GpuResourceMetadataEntry<GpuTextureMetadata>)manager.GetResourceRegistration(view)!;
            Assert.False(manager.RefreshMetadata(textureEntry, GpuMetadataChange.None));
            Assert.Throws<InvalidOperationException>(() => manager.PublishMetadata(textureEntry, textureEntry.Metadata, GpuMetadataChange.None));
            Assert.False(manager.IsResourceMetadataAvailable(view));
            Assert.False(view.IsValid);
            // Retire the synthetic registry-only replacement before the driver may reuse this native name.
            manager.ObserveDeletion(GpuResourceKind.Buffer, oldId);
            using var newBuffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
            newBuffer.Allocate(128);
            GL.TextureBuffer(view.TextureId, SizedInternalFormat.R32f, newBuffer.BufferId);
            Assert.True(manager.IsResourceMetadataAvailable(view));
            Assert.True(view.IsValid);
            Assert.Equal(newBuffer.BufferId, textureEntry.Metadata.Buffer!.Value.BufferId);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>A raw backing name gains a non-owning registry dependency once and participates in intercepted allocation and deletion.</summary>
    [Fact]
    public void RawBackingBufferIsObservedWithoutWrapperOwnership()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch();
        GL.CreateBuffers(1, out int buffer);
        try
        {
            GL.NamedBufferData(buffer, (IntPtr)64, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            Assert.Null(manager.FindResource(GpuResourceKind.Buffer, buffer));
            using var view = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLTextureIntegration.CreateBufferView(buffer, SizedInternalFormat.R32f, true, 0, 0, null).RequireResource();
            var backing = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.FindResource(GpuResourceKind.Buffer, buffer));
            Assert.Equal(0, backing.AssociationCount);
            Assert.True(backing.IsUsable);
            var viewEntry = manager.GetResourceRegistration(view)!;
            ulong revision = viewEntry.StorageRevision;
            var reader = new CountingReader(backing.Reader!);
            backing.Reader = reader;
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, view, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)view).Target)))]);
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 0, view.TextureId, 0, false, 0, TextureAccess.ReadOnly, (SizedInternalFormat)OpenGLImageValues.Format(((GpuTextureRef)view).RequireMetadata().Buffer!.Value.Format));
            Assert.Equal(0, reader.Reads);
            AllocateNative(buffer, 128);
            Assert.Equal(0, reader.Reads);
            Assert.False(view.IsValid);
            Assert.NotNull(manager.AcquireResource(GpuResourceKind.Buffer, buffer, reader));
            Assert.Equal(1, reader.Reads);
            Assert.True(viewEntry.StorageRevision > revision);
            Assert.True(view.IsValid);
            Assert.Equal(0, view.SizeBytes);
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, buffer));
            Assert.False(view.IsValid);
            Assert.False(manager.IsResourceMetadataAvailable(view));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, buffer)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Creates the range policy whose zero-size whole-buffer convention must survive observation.</summary>
    private static GpuBufferView CreateView(GpuBufferObject buffer, bool ranged) => ranged
        ? RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = false, Offset = 0, Length = 64, Label = null }).RequireResource()
        : RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = TextureFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();

    /// <summary>Selects the owning allocation API or the production-transpiled native allocation boundary.</summary>
    private static void Resize(GpuShaderStorageBuffer buffer, int bytes, bool native)
    {
        if (native)
        {
            AllocateNative(buffer.BufferId, bytes);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            Assert.False(manager.IsResourceMetadataAvailable(buffer));
            var entry = (GpuResourceMetadataEntry<GpuBufferMetadata>)manager.GetResourceRegistration(buffer)!;
            if (manager.AcquireResource(GpuResourceKind.Buffer, buffer.BufferId, entry.Reader ?? new GpuBufferMetadataReader()) is null)
                throw new InvalidOperationException("Explicit backing acquisition failed.");
        }
        else buffer.Allocate(bytes);
    }

    /// <summary>Forwards real named buffer storage through the exact production call-site adapter.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateNative(int buffer, int bytes) => GL.NamedBufferData(buffer, (IntPtr)bytes, IntPtr.Zero, BufferUsageHint.DynamicDraw);

    /// <summary>Installs the production transpiler only on this fixture's allocation call site.</summary>
    private static Harmony Patch()
    {
        var harmony = new Harmony("VGE.Tests.BufferViewDependencies");

        return harmony;
    }

    /// <summary>Models an observation failure after a real native storage change.</summary>
    private sealed class UnavailableReader : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        /// <inheritdoc />
        public bool IsResource(nint id) => true;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata data) { data = default; return false; }
    }

    /// <summary>Counts metadata acquisition without changing native reader behavior.</summary>
    private sealed class CountingReader(IGpuResourceMetadataReader<GpuBufferMetadata> inner) : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        internal int Reads;
        /// <inheritdoc />
        public bool IsResource(nint id) => inner.IsResource(id);
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata data) { Reads++; return inner.TryRead(id, out data); }
    }
    /// <summary>Receives the same texture association snapshot without owning the native object.</summary>
    private sealed class TextureAlias(int id) : GpuResource<GpuTextureMetadata>
    {
        /// <summary>Exposes the manager-delivered association.</summary>
        internal GpuTextureMetadata Snapshot => metadata;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; } = id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
    }
    #endregion
}
