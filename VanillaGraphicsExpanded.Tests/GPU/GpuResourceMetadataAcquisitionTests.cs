using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies native metadata acquisition and originating-context retirement of registry primitives.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
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
        fixture.EnsureContextValid();
        using var manager = new GpuResourceManager();
        int buffer = GL.GenBuffer(), sentinel = GL.GenBuffer();
        int previous = GL.GetInteger(GetPName.ArrayBufferBinding);
        try
        {
            var reader = new GpuBufferMetadataReader();
            Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, buffer, reader));
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            GL.BufferData(BufferTarget.ArrayBuffer, 128, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, sentinel);
            manager.RecordPreexistingBuffer(buffer);
            var entry = manager.AcquireResource(GpuResourceKind.Buffer, buffer, reader)!;
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
        fixture.EnsureContextValid();
        using var manager = new GpuResourceManager();
        int buffer = GL.GenBuffer();
        object replacement = new();
        try
        {
            var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, buffer);
            RenderContextRegistry.Retire(fixture);
            RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, entry.State);
            Assert.False(manager.IsRegistered(entry));
            Assert.Throws<ObjectDisposedException>(() => manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, buffer));
            using var nextManager = new GpuResourceManager();
            Assert.NotSame(entry, nextManager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, buffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RenderContextRegistry.Retire(replacement);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            GL.DeleteBuffer(buffer);
        }
    }
    #endregion
}
