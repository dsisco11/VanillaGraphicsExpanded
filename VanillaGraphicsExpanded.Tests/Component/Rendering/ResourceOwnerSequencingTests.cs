using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Exercises owner scheduling independently of per-resource execution guards.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ResourceOwnerSequencingTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A declaration made before startup captures its original cleanup service during association.</summary>
    [Fact]
    public void PreStartupDeclarationQueuesWholeRetirementAfterRealization()
    {
        // Arrange: Declare a resource before startup and associate its original cleanup service during realization.
        // Act and assert: Dispose from a worker and retire at the owner boundary, checking identity until finalization.
        fixture.EnsureContextValid(); fixture.RetireResourceServices();
        using var buffer = new GpuVbo(BufferRole.Vertex, BufferUsage.StaticDraw, null);
        Assert.Null(buffer.ResourceManager);
        fixture.InitializeResourceDisposal();
        try
        {
            var manager = RHIResourceManagerSystem.CurrentManager!;
            Assert.NotNull(RHIResourceManager.CreateBuffer(new GpuBufferInitialization
                { Mode = GpuBufferStorageMode.Mutable, Capacity = 16, Usage = BufferUsage.StaticDraw }, buffer, out _));
            Assert.Same(manager, buffer.ResourceManager);
            nint identity = buffer.ResourceId;
            Exception? failure = null;
            var worker = new Thread(() =>
            {
                try { buffer.Dispose(); }
                catch (Exception error) { failure = error; }
            });
            worker.Start(); worker.Join();
            Assert.Null(failure); Assert.False(buffer.IsDisposed);
            Assert.Equal(identity, buffer.ResourceId);
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            Assert.True(buffer.IsDisposed);
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Owners execute or discard recordings before the scheduled retirement boundary.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerRequestPreservesRecordedInputsUntilOwnerFinishes(bool discard)
    {
        // Arrange: Record a buffer clear, then request disposal from a worker before the owner consumes that recording.
        // Act and assert: Execute or discard the command list before scheduled retirement and verify recorded inputs remain valid.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var commands = RHICommandContext.Current;
            var manager = RHIResourceManagerSystem.CurrentManager!;
            using var buffer = commands.CreateVertexBuffer(new RHIBufferDescription
                { Role = BufferRole.Vertex, Capacity = 16, Mode = ResourceStorageMode.Mutable,
                    Usage = BufferUsage.StaticDraw }).RequireResource();
            using var list = commands.CreateCommandList();
            list.ClearBuffer(buffer, 0, 16, 7); list.Close();
            nint identity = buffer.ResourceId;
            Exception? failure = null;
            var worker = new Thread(() =>
            {
                try { buffer.Dispose(); buffer.Dispose(); }
                catch (Exception error) { failure = error; }
            });
            worker.Start(); worker.Join();
            Assert.Null(failure);
            Assert.False(buffer.IsDisposed);
            Assert.Equal(identity, buffer.ResourceId);
            Assert.True(buffer.IsValid);
            if (discard) list.Cancel();
            else Assert.Equal(CommandListState.Executed, commands.Submit(list).State);
            // Completion here is CPU dispatch only. Native deletion and allocator reuse retain
            // their existing GPU semantics; a recording never owns the referenced allocation.
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            Assert.True(buffer.IsDisposed);
            Assert.Equal((nint)0, buffer.ResourceId);
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Reentrant lifecycle callbacks cannot resize or drain retirement inside a rendering boundary.</summary>
    [Fact]
    public void LifecycleWorkRunsOnlyAfterEnclosingBoundary()
    {
        // Arrange: Register resize work and enter a rendering boundary with active resource ownership.
        // Act and assert: Reject reentrant resize/drain, then allow both after the boundary exits.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int resized = 0;
        var unregister = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, () => resized++);
        try
        {
            var manager = RHIResourceManagerSystem.CurrentManager!;
            using (var scope = RHICommandContext.Current.EnterBoundary(new("Owner interval", true)))
            {
                Assert.Throws<InvalidOperationException>(ScreenResourceManager.HandleScreenResize);
                Assert.Throws<InvalidOperationException>(() => manager.OnRenderFrame(0, RHIResourceManager.Stage));
                Assert.Equal(0, resized);
            }
            ScreenResourceManager.HandleScreenResize();
            Assert.Equal(1, resized);
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
        }
        finally { unregister(); fixture.CleanupGpuResources(); }
    }
    #endregion
}
