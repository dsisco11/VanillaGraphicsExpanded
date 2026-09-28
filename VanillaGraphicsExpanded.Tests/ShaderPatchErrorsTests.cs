using Moq;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks local shader diagnostics across startup and world shutdown boundaries.</summary>
public sealed class ShaderPatchErrorsTests
{
    #region Reporting
    /// <summary>Full diagnostics are logged immediately while startup chat is deferred and deduplicated.</summary>
    [Fact]
    public void StartupFailuresLogAndWaitForWorld()
    {
        var fixture = new ReporterFixture();
        using var reporter = new ShaderPatchErrors(fixture.Api.Object);
        reporter.Report("entityanimated", "missing animation definition");
        reporter.Report("entityanimated", "another stage diagnostic");
        fixture.Logger.Verify(x => x.Error(It.Is<string>(s => s.Contains("missing animation definition"))), Times.Once);
        fixture.Drain();
        fixture.Api.Verify(x => x.ShowChatMessage(It.IsAny<string>()), Times.Never);
        fixture.Events.Raise(x => x.LevelFinalize += null);
        fixture.Api.Verify(x => x.ShowChatMessage(It.Is<string>(s => s.Contains("entityanimated") && s.Contains("client-main.log"))), Times.Once);
    }

    /// <summary>Reload errors use the main thread and defer again after leaving the world.</summary>
    [Fact]
    public void ReadyAndLeaveWorldControlDelivery()
    {
        var fixture = new ReporterFixture();
        using var reporter = new ShaderPatchErrors(fixture.Api.Object);
        fixture.Events.Raise(x => x.LevelFinalize += null);
        reporter.Report("sky", "compile failure");
        fixture.Api.Verify(x => x.ShowChatMessage(It.IsAny<string>()), Times.Never);
        fixture.Drain();
        fixture.Api.Verify(x => x.ShowChatMessage(It.IsAny<string>()), Times.Once);
        fixture.Events.Raise(x => x.LeaveWorld += null);
        reporter.Report("standard", "compile failure");
        fixture.Drain();
        fixture.Api.Verify(x => x.ShowChatMessage(It.IsAny<string>()), Times.Once);
        fixture.Events.Raise(x => x.LevelFinalize += null);
        fixture.Api.Verify(x => x.ShowChatMessage(It.IsAny<string>()), Times.Exactly(2));
    }

    /// <summary>Queued callbacks cannot publish after disposal, and lifecycle handlers are removed.</summary>
    [Fact]
    public void DisposeCancelsQueuedNotification()
    {
        var fixture = new ReporterFixture();
        var reporter = new ShaderPatchErrors(fixture.Api.Object);
        fixture.Events.Raise(x => x.LevelFinalize += null);
        reporter.Report("sky", "compile failure");
        reporter.Dispose();
        fixture.Drain();
        fixture.Events.Raise(x => x.LevelFinalize += null);
        fixture.Api.Verify(x => x.ShowChatMessage(It.IsAny<string>()), Times.Never);
        fixture.Events.VerifyRemove(x => x.LevelFinalize -= It.IsAny<Action>(), Times.Once);
        fixture.Events.VerifyRemove(x => x.LeaveWorld -= It.IsAny<Action>(), Times.Once);
    }
    #endregion

    #region Fixture
    /// <summary>Captures queued client work without starting engine rendering or chat.</summary>
    private sealed class ReporterFixture
    {
        internal Mock<ICoreClientAPI> Api { get; } = new();
        internal Mock<IClientEventAPI> Events { get; } = new();
        internal Mock<ILogger> Logger { get; } = new();
        private readonly Queue<Action> queued = new();

        /// <summary>Connects logger and lifecycle dependencies and retains render-thread actions.</summary>
        internal ReporterFixture()
        {
            Api.SetupGet(x => x.Event).Returns(Events.Object);
            Api.SetupGet(x => x.Logger).Returns(Logger.Object);
            Events.Setup(x => x.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
                .Callback<Action, string>((action, _) => queued.Enqueue(action));
        }

        /// <summary>Executes work explicitly to verify that reporting is marshaled to the client thread.</summary>
        internal void Drain()
        {
            while (queued.TryDequeue(out Action? action)) action();
        }
    }
    #endregion
}
