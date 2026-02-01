using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Cache.ArtifactSystem;

using Vintagestory.API.Client;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Cache.ArtifactSystem;

public sealed class ArtifactSchedulerIdleTests
{
    [Fact]
    public async Task WaitForIdleAsync_WhenNoWork_ReturnsImmediately()
    {
        IClientEventAPI eventsApi = QueuedClientEventApiProxy.Create(out QueuedClientEventApiProxy events);
        ICoreClientAPI capi = CoreClientApiProxy.Create(eventsApi);

        var scheduler = new ArtifactScheduler<int, int>(
            capi,
            computer: new ImmediateComputer(requiresApply: false),
            outputStage: null,
            applier: null,
            maxConcurrency: 1);

        await scheduler.WaitForIdleAsync();
    }

    [Fact]
    public async Task WaitForIdleAsync_WaitsForApplyQueueToDrain()
    {
        IClientEventAPI eventsApi = QueuedClientEventApiProxy.Create(out QueuedClientEventApiProxy events);
        ICoreClientAPI capi = CoreClientApiProxy.Create(eventsApi);

        int applied = 0;

        var scheduler = new ArtifactScheduler<int, int>(
            capi,
            computer: new ImmediateComputer(requiresApply: true),
            outputStage: null,
            applier: new CountingApplier(() => Interlocked.Increment(ref applied)),
            maxConcurrency: 1);

        scheduler.Start();

        Assert.True(scheduler.Enqueue(new WorkItem(1)));

        // Wait until the apply callback is enqueued to the (fake) main-thread queue.
        Assert.True(await WaitUntilAsync(() => events.PendingCount > 0, timeoutMs: 2000));

        Task waitTask = scheduler.WaitForIdleAsync();

        // Should not become idle while apply is still pending.
        Task delay = Task.Delay(150);
        Task finished = await Task.WhenAny(waitTask, delay);
        Assert.Same(delay, finished);
        Assert.Equal(0, Volatile.Read(ref applied));

        events.DrainAll();

        await scheduler.WaitForIdleAsync(new CancellationTokenSource(millisecondsDelay: 2000).Token);
        Assert.Equal(1, Volatile.Read(ref applied));

        scheduler.Stop();
    }

    [Fact]
    public async Task WaitForIdleAsync_WhenStopped_Returns()
    {
        IClientEventAPI eventsApi = QueuedClientEventApiProxy.Create(out QueuedClientEventApiProxy _);
        ICoreClientAPI capi = CoreClientApiProxy.Create(eventsApi);

        var scheduler = new ArtifactScheduler<int, int>(
            capi,
            computer: new ImmediateComputer(requiresApply: true),
            outputStage: null,
            applier: new CountingApplier(() => { }),
            maxConcurrency: 1);

        scheduler.Start();
        Assert.True(scheduler.Enqueue(new WorkItem(1)));

        scheduler.Stop();

        using var cts = new CancellationTokenSource(millisecondsDelay: 2000);
        await scheduler.WaitForIdleAsync(cts.Token);
    }

    [Fact]
    public void FinishOnCurrentThread_DrainsQueue_AndRunsApplyInline()
    {
        IClientEventAPI eventsApi = QueuedClientEventApiProxy.Create(out QueuedClientEventApiProxy events);
        ICoreClientAPI capi = CoreClientApiProxy.Create(eventsApi);

        int applied = 0;

        var scheduler = new ArtifactScheduler<int, int>(
            capi,
            computer: new ImmediateComputer(requiresApply: true),
            outputStage: null,
            applier: new CountingApplier(() => Interlocked.Increment(ref applied)),
            maxConcurrency: 1);

        Assert.True(scheduler.Enqueue(new WorkItem(1)));
        Assert.True(scheduler.Enqueue(new WorkItem(2)));

        scheduler.FinishOnCurrentThread();

        Assert.Equal(2, Volatile.Read(ref applied));
        Assert.Equal(0, events.PendingCount);
    }

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

    private sealed class WorkItem : IArtifactWorkItem<int>
    {
        public WorkItem(int key)
        {
            Key = key;
        }

        public int Key { get; }

        public int Priority => 0;

        public string TypeId => "Test";

        public string DebugLabel => Key.ToString();

        public ArtifactOutputKinds RequiredOutputKinds => ArtifactOutputKinds.None;
    }

    private sealed class ImmediateComputer : IArtifactComputer<int, int>
    {
        private readonly bool requiresApply;

        public ImmediateComputer(bool requiresApply)
        {
            this.requiresApply = requiresApply;
        }

        public ValueTask<ArtifactComputeResult<int>> ComputeAsync(ArtifactComputeContext<int> context)
        {
            var result = new ArtifactComputeResult<int>(
                IsNoop: true,
                Output: default,
                RequiresApply: requiresApply);

            return ValueTask.FromResult(result);
        }
    }

    private sealed class CountingApplier : IArtifactApplier<int, int>
    {
        private readonly Action onApply;

        public CountingApplier(Action onApply)
        {
            this.onApply = onApply;
        }

        public void Apply(in ArtifactApplyContext<int, int> context)
        {
            onApply();
        }
    }

    private class QueuedClientEventApiProxy : DispatchProxy
    {
        private readonly ConcurrentQueue<Action> pending = new();

        public int PendingCount => pending.Count;

        public void DrainAll()
        {
            while (pending.TryDequeue(out Action? a))
            {
                a();
            }
        }

        public static IClientEventAPI Create(out QueuedClientEventApiProxy proxy)
        {
            object proxyObj = Create<IClientEventAPI, QueuedClientEventApiProxy>();
            proxy = (QueuedClientEventApiProxy)proxyObj;
            return (IClientEventAPI)proxyObj;
        }

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
    }

    private class CoreClientApiProxy : DispatchProxy
    {
        private IClientEventAPI? events;

        public static ICoreClientAPI Create(IClientEventAPI events)
        {
            object proxy = Create<ICoreClientAPI, CoreClientApiProxy>();
            var typed = (CoreClientApiProxy)proxy;
            typed.events = events;
            return (ICoreClientAPI)proxy;
        }

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
    }
}
