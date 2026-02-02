using VanillaGraphicsExpanded.LumOn.Scene;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.TraceScene;

public sealed class LumonSceneTraceSceneMetricsTests
{
    [Fact]
    public void SetState_TracksQueueSplitAndTotal()
    {
        LumonSceneTraceSceneMetrics.SetState(queueHighLength: 7, queueLowLength: 11, inFlight: 3, appliedRegions: 5, suppressed: 2);

        Assert.Equal(7, LumonSceneTraceSceneMetrics.QueueHighLength);
        Assert.Equal(11, LumonSceneTraceSceneMetrics.QueueLowLength);
        Assert.Equal(18, LumonSceneTraceSceneMetrics.QueueLength);
        Assert.Equal(2, LumonSceneTraceSceneMetrics.SuppressedCooldown);
        Assert.Equal(3, LumonSceneTraceSceneMetrics.InFlight);
        Assert.Equal(5, LumonSceneTraceSceneMetrics.AppliedRegions);
    }
}

