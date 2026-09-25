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
    }

    /// <summary>Starts a fresh diagnostic lifetime when the world is left.</summary>
    private void ResetReadinessDiagnostics()
    {
        diagnosticState = "not-run";
        diagnosticSeedAttempts = diagnosticSeedFailures = diagnosticIndirectAttempts = diagnosticIndirectFailures = 0;
        diagnosticRefreshAttempts = diagnosticRefreshFailures = 0;
        diagnosticCombineFailures = diagnosticReadbackFailures = diagnosticResets = nextDiagnosticLog = 0;
    }
    #endregion
}
