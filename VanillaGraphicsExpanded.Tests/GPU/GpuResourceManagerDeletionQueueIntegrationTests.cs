using System;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuResourceManagerDeletionQueueIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    public GpuResourceManagerDeletionQueueIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void Dispose_FromBackgroundThread_EnqueuesDeletion_AndManagerDrainsOnTick()
    {
        fixture.EnsureContextValid();

        var manager = new GpuResourceManager();
        GpuResourceManagerSystem.Initialize(manager);

        try
        {
            // Establish render thread identity.
            manager.OnRenderFrame(deltaTime: 0f, stage: GpuResourceManager.Stage);

            while (GL.GetError() != ErrorCode.NoError)
            {
            }

            var vbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, "VGE_Test_DisposeQueue_VBO");
            int id = vbo.BufferId;

            Assert.True(id != 0);
            // glGenBuffers reserves names; the buffer object exists after first bind in core profiles.
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

            Assert.Null(bgException);

            // Drain the manager queues on the render thread.
            manager.OnRenderFrame(deltaTime: 0f, stage: GpuResourceManager.Stage);

            Assert.False(GL.IsBuffer(id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GpuResourceManagerSystem.Shutdown();
            manager.Dispose();
            TextureStreamingSystem.Dispose();
        }
    }
    /// <summary>Queued program deletion cannot cross a retired registration even when the native namespace is unchanged.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgramDeletionUsesOriginatingRegistration(bool queued)
    {
        fixture.EnsureContextValid();
        var manager = new GpuResourceManager();
        GpuResourceManagerSystem.Initialize(manager);
        int id = 0;
        object replacement = new();
        try
        {
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
            var program = GpuProgramObject.Create(); id = program.ProgramId;
            if (queued)
            {
                Exception? failure = null;
                var thread = new Thread(() => { try { program.Dispose(); } catch (Exception error) { failure = error; } });
                thread.Start(); thread.Join(); Assert.Null(failure);
            }
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(fixture);
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
            if (!queued) program.Dispose();
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
            manager.Dispose();
            // Actual GL namespace is deliberately unchanged; its live name represents a reused-context sentinel.
            Assert.True(GL.IsProgram(id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(replacement);
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            if (id != 0) GL.DeleteProgram(id);
            manager.Dispose(); GpuResourceManagerSystem.Shutdown(); TextureStreamingSystem.Dispose();
        }
    }

    /// <summary>Buffer retirement abandons only the dead originating registration and leaves other resources deletable.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BufferDeletionUsesOriginatingRegistration(bool queued)
    {
        fixture.EnsureContextValid();
        var manager = new GpuResourceManager();
        GpuResourceManagerSystem.Initialize(manager);
        int id = 0;
        object replacement = new();
        try
        {
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
            var buffer = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw);
            buffer.Bind(); buffer.Unbind(); id = buffer.BufferId;
            if (queued)
            {
                var thread = new Thread(buffer.Dispose); thread.Start(); thread.Join();
            }
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(fixture);
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(replacement, static _ => true);
            if (!queued) buffer.Dispose();
            manager.Dispose();
            // Registration changes emulate a reused namespace while keeping a live sentinel observable.
            Assert.True(GL.IsBuffer(id));
            using (var current = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw))
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
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            if (id != 0) StateCache.Current.DeleteBuffer(id);
            manager.Dispose(); GpuResourceManagerSystem.Shutdown(); TextureStreamingSystem.Dispose();
        }
    }

    /// <summary>Live queued program retirement drains on its registered origin even before the first renderer tick.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedProgramDeletionDrainsOnOriginatingContext(bool ticked)
    {
        fixture.EnsureContextValid();
        var manager = new GpuResourceManager();
        GpuResourceManagerSystem.Initialize(manager);
        try
        {
            if (ticked) manager.OnRenderFrame(0, GpuResourceManager.Stage);
            var program = GpuProgramObject.Create(); int id = program.ProgramId;
            var thread = new Thread(program.Dispose); thread.Start(); thread.Join();
            Assert.True(GL.IsProgram(id));
            if (ticked) manager.OnRenderFrame(0, GpuResourceManager.Stage);
            else manager.Dispose();
            Assert.False(GL.IsProgram(id));
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { manager.Dispose(); GpuResourceManagerSystem.Shutdown(); TextureStreamingSystem.Dispose(); }
    }
}
