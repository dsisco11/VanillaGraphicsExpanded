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
}
