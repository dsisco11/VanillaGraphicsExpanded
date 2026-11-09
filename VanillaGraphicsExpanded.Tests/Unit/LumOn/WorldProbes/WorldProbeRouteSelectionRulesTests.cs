using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Verifies route selection and fair result dequeue between controlled backends.</summary>
public sealed class WorldProbeRouteSelectionRulesTests
{
    #region Public API
    /// <summary>Only enabled level-zero admission selects the GPU backend and retains the submitted work item.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RoutesOriginalAdmissionByLevel(bool enabled)
    {
        // Arrange: capture accepted work in CPU and GPU route doubles.
        var cpu = new Backend();
        var gpu = new Backend();
        using (var router = new LumOnWorldProbeTraceRouter(enabled, cpu, gpu))
        {
            // Act and Assert: offer all levels while rejecting one selected admission each time.
            for (int level = 0; level < 3; level++)
            {
                var item = new LumOnWorldProbeTraceWorkItem(17, new(level, new(), new(), 3, Ticket: 123), new(1, 2, 3), 99, 16, 7, true, .3f, 2, .001f, 5, true, 42);
                var selected = enabled && level == 0 ? gpu : cpu;
                Assert.True(router.TryEnqueue(item));
                Assert.Equal(item, selected.Items[^1]);
                selected.Accept = false;
                Assert.False(router.TryEnqueue(item));
                selected.Accept = true;
            }

            // Assert: only level-zero enabled work reaches the GPU route.
 
            Assert.Equal(enabled ? 1 : 0, gpu.Items.Count);
            Assert.Equal(enabled ? 2 : 3, cpu.Items.Count);
        }
        Assert.Equal(1, cpu.Disposals);
        Assert.Equal(1, gpu.Disposals);
    }

    /// <summary>Available completion queues alternate fairly and preserve each result's metadata.</summary>
    [Fact]
    public void AlternatesCompletionsAndFallsBackToNonemptyQueue()
    {
        // Arrange: queue two GPU results and one CPU result in known order.
        var cpu = new Backend();
        var gpu = new Backend();
        using var router = new LumOnWorldProbeTraceRouter(true, cpu, gpu);
        var first = default(LumOnWorldProbeTraceResult) with { FrameIndex = 1, SurfaceRevision = 71 };
        var second = first with { FrameIndex = 2 };
        var third = first with { FrameIndex = 3 };
        gpu.Results.Enqueue(first);
        gpu.Results.Enqueue(third);
        cpu.Results.Enqueue(second);

        // Act and assert: dequeue alternates when both routes have work then uses the remaining GPU queue.
        foreach (var expected in new[] { first, second, third })
        {
            Assert.True(router.TryDequeueResult(out var actual));
            Assert.Equal(expected, actual);
        }
        Assert.False(router.TryDequeueResult(out _));
    }

    #endregion
    #region Private
    /// <summary>Records admissions and supplies deterministic completions without running worker code.</summary>
    private sealed class Backend : IWorldProbeTraceBackend
    {
        /// <summary>Stores accepted work items.</summary>
        public List<LumOnWorldProbeTraceWorkItem> Items { get; } = [];

        /// <summary>Stores queued completion values.</summary>
        public Queue<LumOnWorldProbeTraceResult> Results { get; } = [];

        /// <summary>Controls whether new work is accepted.</summary>
        public bool Accept { get; set; } = true;

        /// <summary>Counts disposal calls.</summary>
        public int Disposals { get; private set; }

        /// <summary>Records only accepted admissions.</summary>
        public bool TryEnqueue(in LumOnWorldProbeTraceWorkItem item)
        {
            if (!Accept) return false;
            Items.Add(item);
            return true;
        }

        /// <summary>Dequeues one completion when available.</summary>
        public bool TryDequeueResult(out LumOnWorldProbeTraceResult result) => Results.TryDequeue(out result);

        /// <summary>Records retirement.</summary>
        public void Dispose() => Disposals++;
    }
    #endregion
}











