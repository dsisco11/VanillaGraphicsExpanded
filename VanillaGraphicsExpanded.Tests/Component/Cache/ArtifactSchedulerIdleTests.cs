using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Cache.ArtifactSystem;

using Vintagestory.API.Client;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.Cache;

/// <summary>Checks scheduler idle and inline-drain behavior with a controlled client-event queue.</summary>
public sealed class ArtifactSchedulerIdleTests
{
    #region Public API
    /// <summary>Completes immediately when the scheduler has no queued work.</summary>
    [Fact]
    public async Task WaitForIdleAsync_WhenNoWork_ReturnsImmediately()
    {
        // Arrange: construct a scheduler with no submitted work and a controllable event queue.
        IClientEventAPI eventsApi = QueuedClientEventApiProxy.Create(out QueuedClientEventApiProxy events);
        ICoreClientAPI capi = CoreClientApiProxy.Create(eventsApi);

        var scheduler = new ArtifactScheduler<int, int>(
            capi,
            computer: new ImmediateComputer(requiresApply: false),
            outputStage: null,
            applier: null,
            maxConcurrency: 1);

        // Act: wait for the idle scheduler.
        await scheduler.WaitForIdleAsync();

        // Assert: completion proves no work or callback is required to become idle.
    }

    /// <summary>Waits for a queued main-thread apply callback before reporting idle.</summary>
    [Fact]
    public async Task WaitForIdleAsync_WaitsForApplyQueueToDrain()
    {
        // Arrange: queue a worker result whose apply callback remains under test control.
        IClientEventAPI eventsApi = QueuedClientEventApiProxy.Create(out QueuedClientEventApiProxy events);
        ICoreClientAPI capi = CoreClientApiProxy.Create(eventsApi);

        int applied = 0;

        var scheduler = new ArtifactScheduler<int, int>(
            capi,
            computer: new ImmediateComputer(requiresApply: true),
            outputStage: null,
            applier: new CountingApplier(() => Interlocked.Increment(ref applied)),
            maxConcurrency: 1);

        try
        {
            // Act: start work and wait until the apply callback reaches the controlled main-thread queue.
            scheduler.Start();
            Assert.True(scheduler.Enqueue(new WorkItem(1)));

            // Assert: the scheduler stays non-idle until its pending apply callback runs.
            Assert.True(await WaitUntilAsync(() => events.PendingCount > 0, timeoutMs: 2000));
            Task waitTask = scheduler.WaitForIdleAsync();
            Task delay = Task.Delay(150);
            Task finished = await Task.WhenAny(waitTask, delay);
            Assert.Same(delay, finished);
            Assert.Equal(0, Volatile.Read(ref applied));

            // Act: release the controlled callback, then wait for scheduler quiescence.
            events.DrainAll();
            using var waitCts = new CancellationTokenSource(millisecondsDelay: 2000);
            await scheduler.WaitForIdleAsync(waitCts.Token);

            // Assert: the callback applied exactly once before the scheduler became idle.
            Assert.Equal(1, Volatile.Read(ref applied));
        }
        finally
        {
            scheduler.Stop();
        }
    }

    /// <summary>Completes the idle wait after the scheduler has stopped.</summary>
    [Fact]
    public async Task WaitForIdleAsync_WhenStopped_Returns()
    {
        // Arrange: queue work on a scheduler that will be stopped before completion is observed.
        IClientEventAPI eventsApi = QueuedClientEventApiProxy.Create(out QueuedClientEventApiProxy _);
        ICoreClientAPI capi = CoreClientApiProxy.Create(eventsApi);

        var scheduler = new ArtifactScheduler<int, int>(
            capi,
            computer: new ImmediateComputer(requiresApply: true),
            outputStage: null,
            applier: new CountingApplier(() => { }),
            maxConcurrency: 1);

        try
        {
            // Act: stop the scheduler and await its idle state with a bounded cancellation token.
            scheduler.Start();
            Assert.True(scheduler.Enqueue(new WorkItem(1)));
            scheduler.Stop();

            using var cts = new CancellationTokenSource(millisecondsDelay: 2000);
            await scheduler.WaitForIdleAsync(cts.Token);

            // Assert: the bounded wait completes after Stop cancels work and signals quiescence.
        }
        finally
        {
            scheduler.Stop();
        }
    }

    /// <summary>Drains queued work and runs apply callbacks inline on the calling thread.</summary>
    [Fact]
    public void FinishOnCurrentThread_DrainsQueue_AndRunsApplyInline()
    {
        // Arrange: queue two results whose application can be counted synchronously.
        IClientEventAPI eventsApi = QueuedClientEventApiProxy.Create(out QueuedClientEventApiProxy events);
        ICoreClientAPI capi = CoreClientApiProxy.Create(eventsApi);

        int applied = 0;

        var scheduler = new ArtifactScheduler<int, int>(
            capi,
            computer: new ImmediateComputer(requiresApply: true),
            outputStage: null,
            applier: new CountingApplier(() => Interlocked.Increment(ref applied)),
            maxConcurrency: 1);

        // Act: finish all queued work on the current thread.
        Assert.True(scheduler.Enqueue(new WorkItem(1)));
        Assert.True(scheduler.Enqueue(new WorkItem(2)));

        scheduler.FinishOnCurrentThread();

        // Assert: both results were applied inline and none were posted to the event queue.
        Assert.Equal(2, Volatile.Read(ref applied));
        Assert.Equal(0, events.PendingCount);
    }
    #endregion

    #region Private
    #region Scheduling Helpers
    /// <summary>Waits briefly for a controlled queue condition and checks once more at timeout.</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, int timeoutMs)
    {
        long t0 = Environment.TickCount64;
        while (Environment.TickCount64 - t0 < timeoutMs)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(5);
        }

        return predicate();
    }

    #endregion
    #region Controlled Scheduler Collaborators
    /// <summary>Provides the scheduler with a minimal identified work item.</summary>
    private sealed class WorkItem : IArtifactWorkItem<int>
    {
        #region Public API
        /// <summary>Creates a work item with the supplied key.</summary>
        public WorkItem(int key)
        {
            Key = key;
        }

        /// <summary>Gets the key identifying this work item.</summary>
        public int Key { get; }

        /// <summary>Gets the work priority.</summary>
        public int Priority => 0;

        /// <summary>Gets the stable work type identifier.</summary>
        public string TypeId => "Test";

        /// <summary>Gets a readable label for this work item.</summary>
        public string DebugLabel => Key.ToString();

        /// <summary>Gets the output kinds required by this item.</summary>
        public ArtifactOutputKinds RequiredOutputKinds => ArtifactOutputKinds.None;
        #endregion
    }

    /// <summary>Completes work synchronously and controls whether an apply callback is required.</summary>
    private sealed class ImmediateComputer : IArtifactComputer<int, int>
    {
        #region Public API
        /// <summary>Creates a computer with the configured apply requirement.</summary>
        public ImmediateComputer(bool requiresApply)
        {
            this.requiresApply = requiresApply;
        }

        /// <summary>Returns a completed no-op result with the configured apply requirement.</summary>
        public ValueTask<ArtifactComputeResult<int>> ComputeAsync(ArtifactComputeContext<int> context)
        {
            var result = new ArtifactComputeResult<int>(
                IsNoop: true,
                Output: default,
                RequiresApply: requiresApply);

            return ValueTask.FromResult(result);
        }
        #endregion

        #region Private
        private readonly bool requiresApply;
        #endregion
    }

    /// <summary>Counts applications by invoking a supplied action.</summary>
    private sealed class CountingApplier : IArtifactApplier<int, int>
    {
        #region Public API
        /// <summary>Creates an applier that invokes the supplied callback.</summary>
        public CountingApplier(Action onApply)
        {
            this.onApply = onApply;
        }

        /// <summary>Invokes the configured callback when the scheduler applies a result.</summary>
        public void Apply(in ArtifactApplyContext<int, int> context)
        {
            onApply();
        }
        #endregion

        #region Private
        private readonly Action onApply;
        #endregion
    }

    /// <summary>Queues client-event callbacks so tests decide when the main-thread queue drains.</summary>
    private class QueuedClientEventApiProxy : DispatchProxy
    {
        #region Public API
        /// <summary>Gets the number of callbacks awaiting execution.</summary>
        public int PendingCount => pending.Count;

        /// <summary>Executes each callback currently queued.</summary>
        public void DrainAll()
        {
            while (pending.TryDequeue(out Action? a))
            {
                a();
            }
        }

        /// <summary>Creates a client-event API proxy and returns its controllable queue owner.</summary>
        public static IClientEventAPI Create(out QueuedClientEventApiProxy proxy)
        {
            object proxyObj = Create<IClientEventAPI, QueuedClientEventApiProxy>();
            proxy = (QueuedClientEventApiProxy)proxyObj;
            return (IClientEventAPI)proxyObj;
        }
        #endregion

        #region Protected
        /// <summary>Captures enqueued main-thread callbacks and returns default values for other members.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            args ??= Array.Empty<object?>();

            if (targetMethod.Name == nameof(IClientEventAPI.EnqueueMainThreadTask))
            {
                if (args.Length > 0 && args[0] is Action a)
                {
                    pending.Enqueue(a);
                }

                return null;
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
        #endregion

        #region Private
        private readonly ConcurrentQueue<Action> pending = new();
        #endregion
    }

    /// <summary>Provides a core client API proxy backed by the controlled event API.</summary>
    private class CoreClientApiProxy : DispatchProxy
    {
        #region Public API
        /// <summary>Creates a core client API proxy backed by the supplied event API.</summary>
        public static ICoreClientAPI Create(IClientEventAPI events)
        {
            object proxy = Create<ICoreClientAPI, CoreClientApiProxy>();
            var typed = (CoreClientApiProxy)proxy;
            typed.events = events;
            return (ICoreClientAPI)proxy;
        }
        #endregion

        #region Protected
        /// <summary>Returns the supplied event API and default values for other members.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;

            if (targetMethod.Name == "get_Event")
            {
                return events;
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
        #endregion

        #region Private
        private IClientEventAPI? events;
        #endregion
    }
    #endregion
    #endregion
}
