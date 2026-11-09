using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn.WorldProbes;

/// <summary>Verifies the configured router delegates tracing to the production CPU worker.</summary>
public sealed class WorldProbeTraceRoutingTests
{
    #region Public API
    /// <summary>CPU routing preserves production integrator samples and result metadata.</summary>
    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)]
    [InlineData(true, 1)] [InlineData(true, 2)]
    public void CpuRoutesPreserveRealWorkerResults(bool enabled, int level)
    {
        // Arrange: use the production CPU integrator directly as the baseline for the configured router.
        var world = new VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures.ControlledVoxelWorld();
        world.AddRoom((0, 32, 0), (7, 39, 7), materialId: 17);
        var scene = world.CreateTraceScene();
        var item = new LumOnWorldProbeTraceWorkItem(17, new(level, new(), new(), 3, Ticket: 123),
            new(3.5, 35.5, 3.5), 16, 8, 16, true, .3f, 2, .001f, 1, true, 42);
        var expected = new LumOnWorldProbeTraceIntegrator().TraceProbe(scene, item, CancellationToken.None);
        Assert.True(expected.Success);
        int claims = 0;
        var cpu = new LumOnWorldProbeTraceService(scene, 8, (request, frame) =>
        {
            Assert.Equal(item.Request, request);
            Assert.Equal(item.FrameIndex, frame);
            Interlocked.Increment(ref claims);
            return true;
 
        });
        var gpu = new Backend { Accept = false };
        using var router = new LumOnWorldProbeTraceRouter(enabled, cpu, gpu);

        // Act: enqueue the authored item and wait for the component's asynchronous CPU completion.
        Assert.True(router.TryEnqueue(item));
        LumOnWorldProbeTraceResult actual = default;
        Assert.True(SpinWait.SpinUntil(() => router.TryDequeueResult(out actual), TimeSpan.FromSeconds(3)));

        // Assert: the CPU owner returned the same traced data and the GPU backend was not used.
        Assert.Equal(1, claims);
        Assert.Empty(gpu.Items);
        Assert.Equal(expected.AtlasSamples.Length, actual.AtlasSamples.Length);
        for (int index = 0; index < expected.AtlasSamples.Length; index++)
            Assert.Equal(expected.AtlasSamples[index], actual.AtlasSamples[index]);
        Assert.Equal(expected with { AtlasSamples = default }, actual with { AtlasSamples = default });
        Assert.Null(actual.GpuLease);
        Assert.Null(actual.GpuIdentity);
    }

    #endregion
    #region Private
    /// <summary>Provides a controlled unused GPU route while recording attempted admissions.</summary>
    private sealed class Backend : IWorldProbeTraceBackend
    {
        /// <summary>Stores work accepted by this test backend.</summary>
        public List<LumOnWorldProbeTraceWorkItem> Items { get; } = [];

        /// <summary>Stores deterministic completions supplied by test scenarios.</summary>
        public Queue<LumOnWorldProbeTraceResult> Results { get; } = [];

        /// <summary>Controls whether this backend accepts work.</summary>
        public bool Accept { get; set; } = true;

        /// <summary>Counts owner disposal.</summary>
        public int Disposals { get; private set; }

        /// <summary>Records accepted work without executing a worker.</summary>
        public bool TryEnqueue(in LumOnWorldProbeTraceWorkItem item)
        {
            if (!Accept) return false;
            Items.Add(item);
            return true;
        }

        /// <summary>Returns each queued test completion once.</summary>
        public bool TryDequeueResult(out LumOnWorldProbeTraceResult result) => Results.TryDequeue(out result);

        /// <summary>Records retirement of the controlled backend.</summary>
        public void Dispose() => Disposals++;
    }
    #endregion
}










