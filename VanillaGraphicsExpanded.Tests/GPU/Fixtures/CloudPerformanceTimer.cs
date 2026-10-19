using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes actual production pass boundaries without adding runtime measurement hooks.</summary>
internal sealed class CloudPerformanceTimer : IDisposable
{
    private readonly Harmony patches = new("VGE.Tests.CloudPerformanceTimer");
    private readonly GpuTimestampQuery[] stamps = Enumerable.Range(0, 9).Select(_ => GpuTimestampQuery.Create()).ToArray();
    [ThreadStatic] private static CloudPerformanceTimer? active;
    private int pass;

    #region Public API
    /// <summary>Installs scoped test-only timestamp observers on the existing immediate graphics boundary.</summary>
    internal CloudPerformanceTimer()
    {
        patches.Patch(AccessTools.Method(typeof(GraphicsCommandContext), "BeginPass"),
            prefix: new HarmonyMethod(typeof(CloudPerformanceTimer), nameof(BeginPass)));
        patches.Patch(AccessTools.Method(typeof(GraphicsCommandContext), "EndPass"),
            postfix: new HarmonyMethod(typeof(CloudPerformanceTimer), nameof(EndPass)));
    }

    /// <summary>Begins an outer total interval before hierarchy construction and production draw setup.</summary>
    internal void Begin()
    {
        Assert.Null(active); pass = 0; active = this; stamps[0].Issue();
    }

    /// <summary>Separates optional reduction from unchanged production reconstruction setup.</summary>
    internal void ConstructionComplete() => stamps[1].Issue();

    /// <summary>Closes the whole submitted operation before waiting for any query result.</summary>
    internal double[] End(GpuTimestampQuery? wholeEnd=null)
    {
        stamps[8].Issue(); active = null;
        wholeEnd?.Issue();
        Assert.Equal(3, pass);
        long[] values = stamps.Select(stamp => stamp.GetResultNanoseconds()).ToArray();
        return [(values[1]-values[0])/1e6, (values[3]-values[2])/1e6,
            (values[5]-values[4])/1e6, (values[7]-values[6])/1e6, (values[8]-values[0])/1e6];
    }

    /// <summary>Removes observers before retiring query objects, including exceptional test exits.</summary>
    public void Dispose()
    {
        active = null; patches.UnpatchAll(patches.Id);
        foreach (var stamp in stamps) stamp.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Starts each production pass before attachment setup and shader publication.</summary>
    private static void BeginPass()
    {
        if (active is { } timer) { Assert.InRange(timer.pass, 0, 2); timer.stamps[2+timer.pass*2].Issue(); }
    }

    /// <summary>Ends each production pass after shader and borrowed attachment restoration.</summary>
    private static void EndPass()
    {
        if (active is { } timer) { timer.stamps[3+timer.pass*2].Issue(); timer.pass++; }
    }
    #endregion
}
