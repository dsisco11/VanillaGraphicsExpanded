using Moq;
using VanillaGraphicsExpanded.Rendering.Enablement;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies queued rendering-thread delivery and retirement of pending work.</summary>
public sealed class RenderThreadDispatchTests
{
    #region Public API

    /// <summary>Background changes are queued and become inert when the dispatch owner retires.</summary>
    [Fact]
    public void RenderingDispatchQueuesBackgroundWorkAndCancelsRetiredWork()
    {
        // Arrange: capture native enqueue operations so the test controls when queued work runs.
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var queued = new List<Action>();
        api.Setup(value => value.Event.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((action, _) => queued.Add(action));
        using var dispatch = new RenderThreadDispatch(api.Object);
        int calls = 0;

        // Act: run immediate work, enqueue from a worker, and then execute that queued callback.
        dispatch.Run(() => calls++);
        Assert.Equal(1, calls);
        var thread = new Thread(() => dispatch.Run(() => calls++));
        thread.Start();
        thread.Join();
        Assert.Equal(1, calls);
        Assert.Single(queued);
        queued[0]();
        Assert.Equal(2, calls);

        // Arrange: enqueue a second worker callback, then retire its owner before delivery.
        var pending = new Thread(() => dispatch.Run(() => calls++));
        pending.Start();
        pending.Join();

        // Act: disposal invalidates the second queued callback and all future submissions.
        dispatch.Dispose();
        queued[1]();
        dispatch.Run(() => calls++);

        // Assert: retired queued work and future submissions did not run.
        Assert.Equal(2, calls);
    }

    #endregion
}
