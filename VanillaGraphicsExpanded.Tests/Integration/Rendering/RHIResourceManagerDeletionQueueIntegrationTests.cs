using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Xunit;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks background disposal and original-context retirement through the active RHI manager.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHIResourceManagerDeletionQueueIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    #region Public API
    /// <summary>Retains the shared native context without owning its lifetime.</summary>
    public RHIResourceManagerDeletionQueueIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>Queues background disposal until the originating manager drains on the render thread.</summary>
    [Fact]
    public void Dispose_FromBackgroundThread_EnqueuesDeletion_AndManagerDrainsOnTick()
    {
        // Arrange: create a fresh manager lifetime and assert its first tick leaves no prior GL error.
        fixture.EnsureContextValid();

        var manager = StartManagerLifetime();
        GpuBufferObject? vbo = null;

        try
        {
            // Establish render thread identity.
            manager.OnRenderFrame(deltaTime: 0f, stage: RHIResourceManager.Stage);
            Assert.Equal(ErrorCode.NoError, GL.GetError());

            // Act: dispose the buffer from a worker and drain the queued deletion on the render thread.
            vbo = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StreamDraw, Label = "VGE_Test_DisposeQueue_VBO" }).RequireResource();
            int id = vbo.BufferId;

            Assert.True(id != 0);
            // Managed creation returns a realized buffer; selector changes retain that allocation.
            vbo.Bind();
            vbo.Unbind();
            Assert.True(GL.IsBuffer(id));

            Exception? bgException = null;
            var thread = new Thread(() =>
            {
                try
                {
                    vbo.Dispose();
                }
                catch (Exception ex)
                {
                    bgException = ex;
                }
            })
            {
                IsBackground = true
            };

            thread.Start();
            thread.Join();

            // Assert: background disposal succeeds and the next manager tick deletes the native name.
            Assert.Null(bgException);

            // Drain the manager queues on the render thread.
            manager.OnRenderFrame(deltaTime: 0f, stage: RHIResourceManager.Stage);

            Assert.False(GL.IsBuffer(id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            try
            {
                vbo?.Dispose();
            }
            finally
            {
                RHIResourceManagerSystem.Shutdown();
                manager.Dispose();
                TextureStreamingSystem.Dispose();
            }
        }
    }
    /// <summary>Queued program deletion cannot cross a retired registration even when the native namespace is unchanged.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgramDeletionUsesOriginatingRegistration(bool queued)
    {
        // Arrange: create a native program and an explicit replacement registration for the same GL namespace.
        fixture.EnsureContextValid();
        var manager = StartManagerLifetime();
        int id = 0;
        object replacement = new();
        try
        {
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            var program = NativeResourceSetup.CreateProgram(); id = program.ProgramId;
            // Act: retire through either the worker queue or the current thread after retiring its origin.
            if (queued)
            {
                Exception? failure = null;
                var thread = new Thread(() => { try { program.Dispose(); } catch (Exception error) { failure = error; } });
                thread.Start(); thread.Join(); Assert.Null(failure);
            }
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(fixture);
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
            if (!queued) program.Dispose();
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            manager.Dispose();
            // Actual GL namespace is deliberately unchanged; its live name represents a reused-context sentinel.
            // Assert: the stale registration cannot delete a live sentinel with the same native name.
            Assert.True(GL.IsProgram(id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(replacement);
            fixture.RestoreContextRegistration();
            if (id != 0) GL.DeleteProgram(id);
            manager.Dispose(); RHIResourceManagerSystem.Shutdown(); TextureStreamingSystem.Dispose();
        }
    }

    /// <summary>Buffer retirement abandons only the dead originating registration and leaves other resources deletable.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BufferDeletionUsesOriginatingRegistration(bool queued)
    {
        // Arrange: create and bind a buffer before replacing its originating registration.
        fixture.EnsureContextValid();
        var manager = StartManagerLifetime();
        int id = 0;
        object replacement = new();
        try
        {
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StreamDraw, Label = null }).RequireResource();
            buffer.Bind(); buffer.Unbind(); id = buffer.BufferId;
            // Act: dispose through the selected thread path, then drain against the replacement context.
            if (queued)
            {
                var thread = new Thread(buffer.Dispose); thread.Start(); thread.Join();
            }
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(fixture);
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
            if (!queued) buffer.Dispose();
            manager.Dispose();
            // Registration changes emulate a reused namespace while keeping a live sentinel observable.
            // Assert: the stale resource remains live while a replacement-owned buffer can still be deleted.
            Assert.True(GL.IsBuffer(id));
            using (var current = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StreamDraw, Label = null }).RequireResource())
            {
                current.Bind(); current.Unbind();
                int currentId = current.BufferId;
                current.Dispose();
                Assert.False(GL.IsBuffer(currentId));
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(replacement);
            fixture.RestoreContextRegistration();
            if (id != 0) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, id));
            manager.Dispose(); RHIResourceManagerSystem.Shutdown(); TextureStreamingSystem.Dispose();
        }
    }

    /// <summary>Live queued program retirement drains on its registered origin even before the first renderer tick.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedProgramDeletionDrainsOnOriginatingContext(bool ticked)
    {
        // Arrange: select whether the fresh manager has processed an initial render tick.
        fixture.EnsureContextValid();
        var manager = StartManagerLifetime();
        try
        {
            if (ticked) manager.OnRenderFrame(0, RHIResourceManager.Stage);
            // Act: queue program disposal from a worker and drain only on its owning context.
            var program = NativeResourceSetup.CreateProgram(); int id = program.ProgramId;
            var thread = new Thread(program.Dispose); thread.Start(); thread.Join();
            Assert.True(GL.IsProgram(id));
            if (ticked) manager.OnRenderFrame(0, RHIResourceManager.Stage);
            else manager.Dispose();
            // Assert: the owner tick retires the native name and leaves no GL error.
            Assert.False(GL.IsProgram(id));
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { manager.Dispose(); RHIResourceManagerSystem.Shutdown(); TextureStreamingSystem.Dispose(); }
    }

    #endregion

    #region Private
    /// <summary>Starts an unticked lifetime after explicitly retiring the fixture's observation manager.</summary>
    private static RHIResourceManager StartManagerLifetime()
    {
        // EnsureContextValid installs native observation and its manager. These tests deliberately
        // own a fresh lifetime, including the pre-first-frame case, so close that service first.
        var previous = RHIResourceManagerSystem.CurrentManager;
        RHIResourceManagerSystem.Shutdown();
        previous?.Dispose();
        var manager = new RHIResourceManager();
        RHIResourceManagerSystem.Initialize(manager);
        return manager;
    }
    #endregion
}
