using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies native metadata acquisition and originating-context retirement of registry primitives.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuResourceMetadataAcquisitionTests
{
    private readonly HeadlessGLFixture fixture;

    #region Public API
    /// <summary>Uses the shared hidden context without starting the game.</summary>
    public GpuResourceMetadataAcquisitionTests(HeadlessGLFixture fixture) { this.fixture = fixture; }

    /// <summary>Realized external buffers acquire actual metadata without binding or granting deletion ownership.</summary>
    [Fact]
    public void BufferAcquisitionReadsNativeFactsAndPreservesBindings()
    {
        // Arrange: create a foreign buffer and preserve a distinct binding as the state sentinel.
        fixture.EnsureContextValid();
        using var manager = new RHIResourceManager();
        int buffer = GL.GenBuffer(), sentinel = GL.GenBuffer();
        int previous = GL.GetInteger(GetPName.ArrayBufferBinding);
        try
        {
            var reader = new GpuBufferMetadataReader();
            Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, buffer, reader));
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            GL.BufferData(BufferTarget.ArrayBuffer, 128, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, sentinel);
            // Act: acquire and refresh metadata for externally created native storage.
            manager.RecordPreexistingBuffer(buffer);
            var entry = manager.AcquireResource(GpuResourceKind.Buffer, buffer, reader)!;

            // Assert: actual capacity and usage are captured without changing the caller's binding.
            Assert.NotNull(entry);
            Assert.Equal(new GpuBufferMetadata(128, BufferUsage.DynamicDraw), entry.Metadata);
            Assert.Equal(sentinel, GL.GetInteger(GetPName.ArrayBufferBinding));
            Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, buffer, reader));
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            GL.BufferData(BufferTarget.ArrayBuffer, 0, IntPtr.Zero, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, sentinel);
            Assert.True(manager.RefreshMetadata(entry, GpuMetadataChange.Storage));
            Assert.Equal(new GpuBufferMetadata(0, BufferUsage.StaticDraw), entry.Metadata);
            manager.Dispose();
            Assert.True(GL.IsBuffer(buffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, previous);
            GL.DeleteBuffer(buffer); GL.DeleteBuffer(sentinel);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>The context registry event invalidates canonical entries even when another owner reuses the native context.</summary>
    [Fact]
    public void ContextRetirementRemovesRegistrationWithoutDeletingNativeObjects()
    {
        // Arrange: register an externally generated native name in the fixture's originating context.
        fixture.EnsureContextValid();
        using var manager = new RHIResourceManager();
        int buffer = GL.GenBuffer();
        object replacement = new();
        try
        {
            var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, buffer);
            // Act: retire that context and register a replacement logical owner for the same current context.
            RenderContextRegistry.Retire(fixture);
            RenderContextRegistry.RegisterCurrent(replacement, static _ => true);

            // Assert: the old registration is withdrawn, but the externally owned GL name is not deleted.
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, entry.State);
            Assert.False(manager.IsRegistered(entry));
            Assert.Throws<ObjectDisposedException>(() => manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, buffer));
            using var nextManager = new RHIResourceManager();
            Assert.NotSame(entry, nextManager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, buffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RenderContextRegistry.Retire(replacement);
            fixture.RestoreContextRegistration();
            GL.DeleteBuffer(buffer);
        }
    }
    #endregion
}
