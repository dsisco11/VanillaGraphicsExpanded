using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Reports first-publication progress using existing completion reads, without extra GPU synchronization.</summary>
internal sealed partial class LumonSceneRelightUpdateRenderer
{
    private string diagnosticState = "not-run";
    private long diagnosticSeedAttempts, diagnosticSeedFailures, diagnosticIndirectAttempts, diagnosticIndirectFailures;
    private long diagnosticRefreshAttempts, diagnosticRefreshFailures;
    private long diagnosticCombineFailures, diagnosticReadbackFailures, diagnosticResets;
    private long nextDiagnosticLog;
    private readonly SurfaceCpuTiming diagnosticRenderTiming = new();
    private readonly SurfacePageProgress diagnosticSeedQueue = new();
    private readonly SurfacePageProgress diagnosticIndirectQueue = new();
    private readonly double[] diagnosticMapMilliseconds = new double[4];
    private readonly long[] diagnosticMapCalls = new long[4];

    #region Readiness diagnostics
    /// <summary>Formats lifetime counters separately from the current producer state.</summary>
    private string ReadinessDiagnosticLine() =>
        $"LSR: {diagnosticState} pages:{lastWorkCount} ready:{publishedPages.Count} generation:{snapshot.Generation} " +
        $"seedFail:{diagnosticSeedFailures}/{diagnosticSeedAttempts} indirectFail:{diagnosticIndirectFailures}/{diagnosticIndirectAttempts} " +
        $"refreshFail:{diagnosticRefreshFailures}/{diagnosticRefreshAttempts} combineFail:{diagnosticCombineFailures} readFail:{diagnosticReadbackFailures} resets:{diagnosticResets}";

    /// <summary>Logs bounded periodic evidence even when producer prerequisites cause early returns.</summary>
    private void ReportReadiness(string state)
    {
        diagnosticState = state;
        long now = Environment.TickCount64;
        if (now < nextDiagnosticLog) return;
        nextDiagnosticLog = now + 10000;
        // Include capture progress so an empty relight queue can be distinguished from failed lighting.
        feedback.TryGetSelfCheckLine(out string capture);
        capi.Logger.Notification("[VGE] Surface cache readiness: {0} {1}", ReadinessDiagnosticLine(), capture);
        feedback.ReportCaptureMeasurements();
        capi.Logger.Notification("[VGE] Surface cache work: {0}",
            SurfaceWorkDiagnosticText.FormatQueue("indirectProgressEligible", diagnosticIndirectQueue, now));
        capi.Logger.Notification("[VGE] Surface cache work: relightRenderWallMs:{0:0.###} frames:{1} {2} " +
            "mapSeedMs:{3:0.###}/{4} mapIndirectMs:{5:0.###}/{6} mapDirectMs:{7:0.###}/{8} mapCombineMs:{9:0.###}/{10}",
            diagnosticRenderTiming.Milliseconds, diagnosticRenderTiming.Count,
            SurfaceWorkDiagnosticText.FormatQueue("seedEligible", diagnosticSeedQueue, now),
            diagnosticMapMilliseconds[0], diagnosticMapCalls[0], diagnosticMapMilliseconds[1], diagnosticMapCalls[1],
            diagnosticMapMilliseconds[2], diagnosticMapCalls[2], diagnosticMapMilliseconds[3], diagnosticMapCalls[3]);
        if (dispatch != null)
            for (var kind = SurfaceWorkStage.Seed; kind < SurfaceWorkStage.Count; kind++)
                capi.Logger.Notification("[VGE] Surface cache work: {0}", SurfaceWorkDiagnosticText.Format(kind, dispatch.Diagnostics.Snapshot(kind)));
    }

    /// <summary>Starts a fresh diagnostic lifetime when the world is left.</summary>
    private void ResetReadinessDiagnostics()
    {
        diagnosticState = "not-run";
        diagnosticSeedAttempts = diagnosticSeedFailures = diagnosticIndirectAttempts = diagnosticIndirectFailures = 0;
        diagnosticRefreshAttempts = diagnosticRefreshFailures = 0;
        diagnosticCombineFailures = diagnosticReadbackFailures = diagnosticResets = nextDiagnosticLog = 0;
        diagnosticRenderTiming.Clear(); diagnosticSeedQueue.Clear(); diagnosticIndirectQueue.Clear();
        Array.Clear(diagnosticMapMilliseconds); Array.Clear(diagnosticMapCalls);
    }
    #endregion
}
