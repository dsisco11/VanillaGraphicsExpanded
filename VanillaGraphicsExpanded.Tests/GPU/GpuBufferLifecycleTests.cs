using System;
using OpenTK.Graphics.OpenGL;
using GLFWBindingsContext = OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies maintained raw buffer lifetimes, provenance and saved-original RHI handoff.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuBufferLifecycleTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Both binding roots maintain reservation, defaults and allocation without invoking a reader.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeFactoriesMaintainOriginalIdentity(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var queries = new BufferDescriptorCapture();
        var manager = RHIResourceManagerSystem.CurrentManager!;
        int id = core ? OpenTK.Graphics.OpenGL4.GL.GenBuffer() : GL.GenBuffer();
        try
        {
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.FindResource(GpuResourceKind.Buffer, id));
            Assert.False(entry.IsRealized); Assert.False(entry.IsUsable);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, id, new ForbiddenReader()));
            if (core) OpenTK.Graphics.OpenGL4.GL.BindBuffer(OpenTK.Graphics.OpenGL4.BufferTarget.ArrayBuffer, id);
            else GL.BindBuffer(BufferTarget.ArrayBuffer, id);
            Assert.True(entry.IsRealized); Assert.True(entry.IsUsable);
            Assert.Equal(new GpuBufferMetadata(0, BufferUsage.StaticDraw), entry.Metadata);
            ulong revision = entry.MetadataRevision;
            GL.BufferData(BufferTarget.ArrayBuffer, 64, IntPtr.Zero, BufferUsageHint.StreamRead);
            Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, id, new ForbiddenReader()));
            Assert.Equal(new GpuBufferMetadata(64, BufferUsage.StreamRead), entry.Metadata);
            Assert.Equal(revision + 1, entry.MetadataRevision); Assert.Equal(1ul, entry.StorageRevision);
            GL.BufferSubData(BufferTarget.ArrayBuffer, 0, 4, new[] { 42 });
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.False(OpenGlStateCache.Current.NeedsBufferBinding(BufferTarget.ArrayBuffer, id));
            Assert.Equal(0, queries.DescriptorReads); Assert.Equal(0, queries.IdentityReads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteBuffer(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Realized core creation publishes once; stale outputs and rejected allocation do not renew it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void CreatedDefaultsAndRejectedRequestsPreserveFacts(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int id;
        if (core) OpenTK.Graphics.OpenGL4.GL.CreateBuffers(1, out id); else GL.CreateBuffers(1, out id);
        var manager = RHIResourceManagerSystem.CurrentManager!;
        try
        {
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.FindResource(GpuResourceKind.Buffer, id));
            Assert.True(entry.IsUsable); Assert.Equal(1ul, entry.MetadataRevision);
            uint stale = unchecked((uint)id);
            OpenGlStateCache.ObserveCreateBuffers(1, &stale);
            OpenGlStateCache.ObserveCreateBuffers(-1, &stale);
            Assert.Same(entry, manager.FindResource(GpuResourceKind.Buffer, id)); Assert.Equal(1ul, entry.MetadataRevision);
            GL.NamedBufferStorage(id, 32, IntPtr.Zero, BufferStorageFlags.MapWriteBit);
            ulong revision = entry.MetadataRevision;
            GL.NamedBufferData(id, 16, IntPtr.Zero, BufferUsageHint.StaticDraw);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.Equal(32, entry.Metadata.Capacity); Assert.Equal(revision, entry.MetadataRevision);
            Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, id, new ForbiddenReader()));
            GL.DeleteBuffer(id);
            Assert.Equal(GpuResourceRegistrationState.Deleted, entry.State);
            GL.CreateBuffers(1, out int replacement);
            try { Assert.NotSame(entry, manager.FindResource(GpuResourceKind.Buffer, replacement)); }
            finally { GL.DeleteBuffer(replacement); }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteBuffer(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>EXT initialization happens even when its subsequent storage request is rejected.</summary>
    [Fact]
    public void ExtRejectedUpdateStillEstablishesDefaults()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        Assert.SkipWhen(!GpuSupport.Supports("GL_EXT_direct_state_access"), "EXT DSA is unavailable.");
        int id = GL.GenBuffer();
        try
        {
            GL.Ext.NamedBufferData(id, -1, IntPtr.Zero, BufferUsageHint.StaticDraw);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Buffer, id));
            Assert.True(entry.IsRealized); Assert.True(entry.IsUsable);
            Assert.Equal(new GpuBufferMetadata(0, BufferUsage.StaticDraw), entry.Metadata);
            Assert.Equal(1ul, entry.MetadataRevision); Assert.Equal(0ul, entry.StorageRevision);
        }
        finally { GL.DeleteBuffer(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Single vertex binding realizes a reserved backing, while multi-bind must reject it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VertexBindingRealizesReservedBuffer(bool named)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateVertexArrays(1, out int vao);
        int id = GL.GenBuffer();
        try
        {
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.FindResource(GpuResourceKind.Buffer, id));
            GL.BindVertexArray(vao);
            GL.BindVertexBuffers(0, 1, new[] { id }, new[] { IntPtr.Zero }, new[] { 16 });
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.False(entry.IsRealized); Assert.False(entry.IsUsable);
            if (named) GL.VertexArrayVertexBuffer(vao, 0, id, IntPtr.Zero, 16);
            else GL.BindVertexBuffer(0, id, IntPtr.Zero, 16);
            Assert.True(entry.IsRealized); Assert.True(entry.IsUsable);
            Assert.Equal(new GpuBufferMetadata(0, BufferUsage.StaticDraw), entry.Metadata);
            GL.NamedBufferData(id, 16, IntPtr.Zero, BufferUsageHint.StaticDraw);
            Assert.Equal((nint)16, entry.Metadata.Capacity);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteVertexArray(vao); GL.DeleteBuffer(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Mapping results belong to execution knowledge, and replacement discards a mutable mapping.</summary>
    [Fact]
    public void MappingAndContentDoNotPublishStorage()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateBuffers(1, out int id);
        try
        {
            GL.NamedBufferData(id, 32, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Buffer, id));
            ulong revision = entry.MetadataRevision;
            Assert.NotEqual(IntPtr.Zero, GL.MapNamedBufferRange(id, 0, 16, BufferAccessMask.MapWriteBit | BufferAccessMask.MapFlushExplicitBit));
            Assert.True(OpenGlStateCache.Current.TryGetBufferMapping(id, out bool mapped, out bool persistent) && mapped && !persistent);
            GL.FlushMappedNamedBufferRange(id, -1, 8);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.True(OpenGlStateCache.Current.TryGetBufferMapping(id, out mapped, out persistent) && mapped && !persistent);
            GL.FlushMappedNamedBufferRange(id, 0, 8);
            Assert.True(GL.UnmapNamedBuffer(id));
            Assert.True(OpenGlStateCache.Current.TryGetBufferMapping(id, out mapped, out persistent) && !mapped && !persistent);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(IntPtr.Zero, GL.MapNamedBufferRange(id, -1, 16, BufferAccessMask.MapWriteBit));
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.True(entry.IsUsable); Assert.Equal(revision, entry.MetadataRevision);
        }
        finally { GL.DeleteBuffer(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Reload retains the manager and original registrations before any explicit RHI operation.</summary>
    [Fact]
    public void ReloadRetainsCreationAuthority()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var manager = RHIResourceManagerSystem.CurrentManager!;
        GL.CreateBuffers(1, out int first);
        var original = manager.FindResource(GpuResourceKind.Buffer, first);
        try
        {
            GL.LoadBindings(new GLFWBindingsContext());
            OpenTK.Graphics.OpenGL4.GL.LoadBindings(new GLFWBindingsContext());
            GL.CreateBuffers(1, out int second);
            try
            {
                Assert.Same(manager, RHIResourceManagerSystem.CurrentManager);
                Assert.Same(original, manager.FindResource(GpuResourceKind.Buffer, first));
                Assert.True(manager.FindResource(GpuResourceKind.Buffer, second)!.IsUsable);
            }
            finally { GL.DeleteBuffer(second); }
        }
        finally { GL.DeleteBuffer(first); fixture.CleanupGpuResources(); }
    }

    /// <summary>Context recreation establishes its replacement manager before a raw native factory is consumed.</summary>
    [Fact]
    public void RecreatedContextHasCreationAuthorityBeforeRhi()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var old = RHIResourceManagerSystem.CurrentManager!;
        GL.CreateBuffers(1, out int first);
        var original = old.FindResource(GpuResourceKind.Buffer, first)!;
        object replacement = new();
        try
        {
            RenderContextRegistry.Retire(fixture);
            RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
            GL.CreateBuffers(1, out int second);
            try
            {
                var manager = RHIResourceManagerSystem.CurrentManager!;
                Assert.NotSame(old, manager);
                Assert.Equal(GpuResourceRegistrationState.ContextRetired, original.State);
                Assert.True(manager.FindResource(GpuResourceKind.Buffer, second)!.IsUsable);
            }
            finally { GL.DeleteBuffer(second); }
        }
        finally
        {
            RenderContextRegistry.Retire(replacement);
            RenderContextRegistry.RegisterCurrent(fixture, static target => ((HeadlessGLFixture)target).IsContextValid);
            GL.DeleteBuffer(first); fixture.CleanupGpuResources();
        }
    }

    /// <summary>Only a pre-hook owner handoff permits one descriptor read; covered association then reuses it.</summary>
    [Fact]
    public void ExplicitPreinstallDiscoveryIsConsumedOnce()
    {
        fixture.EnsureContextValid();
        OpenGLBindingInterception.Uninstall();
        RHIResourceManagerSystem.CurrentManager?.Dispose(); RHIResourceManagerSystem.Shutdown();
        GL.CreateBuffers(1, out int id);
        GL.NamedBufferData(id, 24, IntPtr.Zero, BufferUsageHint.StreamCopy);
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        var original = manager.RecordPreexistingBuffer(id);
        OpenGLBindingInterception.Install();
        try
        {
            var reader = new CountingReader();
            var entry = manager.AcquireResource(GpuResourceKind.Buffer, id, reader)!;
            Assert.Same(original, entry); Assert.Equal(1, reader.Reads);
            Assert.Equal(new GpuBufferMetadata(24, BufferUsage.StreamCopy), entry.Metadata);
            Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, id, new ForbiddenReader()));
            GL.DeleteBuffer(id);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, id, new ForbiddenReader()));
            Assert.Equal(GpuResourceRegistrationState.Deleted, original.State);
        }
        finally { GL.DeleteBuffer(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Failed initial discovery cannot make an existing allocation appear freshly empty on a later bind.</summary>
    [Fact]
    public void FailedPreinstallDiscoveryPreservesUnknownOriginalStorage()
    {
        fixture.EnsureContextValid(); fixture.UseContextLocalResources();
        OpenGLBindingInterception.Uninstall();
        GL.CreateBuffers(1, out int id);
        GL.NamedBufferData(id, 32, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        var entry = manager.RecordPreexistingBuffer(id);
        OpenGLBindingInterception.Install();
        try
        {
            Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, id, new UnavailableReader()));
            Assert.True(entry.IsRealized); Assert.False(entry.IsUsable);
            GL.BindBuffer(BufferTarget.ArrayBuffer, id);
            Assert.False(entry.IsUsable); Assert.Equal(0ul, entry.MetadataRevision);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, id, new ForbiddenReader()));
            Assert.Same(entry, manager.RecoverResource(GpuResourceKind.Buffer, id, new GpuBufferMetadataReader()));
            Assert.Equal(new GpuBufferMetadata(32, BufferUsage.DynamicDraw), entry.Metadata);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteBuffer(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Missing covered identities never become implicit native descriptor discovery.</summary>
    [Fact]
    public void MissingCoveredBufferDoesNotInvokeReader()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var queries = new BufferDescriptorCapture();
        var manager = RHIResourceManagerSystem.CurrentManager!;
        Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, int.MaxValue, new ForbiddenReader()));
        Assert.Null(manager.RecoverResource(GpuResourceKind.Buffer, int.MaxValue, new ForbiddenReader()));
        Assert.Throws<InvalidOperationException>(() => manager.RecordPreexistingBuffer(int.MaxValue));
        using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
            { Role = BufferRole.Vertex, Capacity = 16, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.DynamicDraw }).RequireResource();
        var entry = manager.GetResourceRegistration(buffer)!;
        Assert.True(entry.IsRealized); Assert.Equal(1ul, entry.MetadataRevision);
        Assert.Equal(0, queries.DescriptorReads); Assert.Equal(0, queries.IdentityReads);
        fixture.CleanupGpuResources();
    }
    #endregion

    #region Private
    /// <summary>Counts the single explicitly justified native initial-discovery operation.</summary>
    private sealed class CountingReader : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        internal int Reads;
        /// <inheritdoc />
        public bool IsResource(nint id) => throw new InvalidOperationException("Reader owns native identity admission.");
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata data)
        {
            Reads++;
            return new GpuBufferMetadataReader().TryRead(id, out data);
        }
    }

    /// <summary>Models a failed initial descriptor result without changing the original native allocation.</summary>
    private sealed class UnavailableReader : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        /// <inheritdoc />
        public bool IsResource(nint id) => true;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata data) { data = default; return false; }
    }

    /// <summary>Rejects accidental acquisition while tests associate with maintained snapshots.</summary>
    private sealed class ForbiddenReader : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        /// <inheritdoc />
        public bool IsResource(nint id) => throw new InvalidOperationException("Unexpected object probe.");
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata data) => throw new InvalidOperationException("Unexpected descriptor read.");
    }
    #endregion
}
