using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Reports capture work, first-admission age and existing readback cost separately from lighting.</summary>
internal sealed partial class LumonSceneFeedbackUpdateRenderer
{
    private readonly SurfaceCpuTiming diagnosticCaptureRender = new();
    private readonly SurfacePageProgress diagnosticCaptureQueue = new();
    private double diagnosticCaptureMapMilliseconds;
    private long diagnosticCaptureMapCalls, diagnosticCaptureIdentityFailures;

    #region Reporting
    /// <summary>Emits lifetime aggregates only when the relighter's existing periodic report is due.</summary>
    internal void ReportCaptureMeasurements()
    {
        // Retired physical identities must not age forever in diagnostic queues.
        diagnosticCaptureQueue.Prune((page, key, generation) =>
            virtualToPhysical.TryGetValue(key, out uint current) && current == page &&
            LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key) < slotGenerations.Length &&
            slotGenerations[LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key)] == generation);
        capi.Logger.Notification("[VGE] Surface cache work: feedbackRenderWallMs:{0:0.###} frames:{1} captureMapMs:{2:0.###}/{3} " +
            "captureIdentityReject:{4} {5}", diagnosticCaptureRender.Milliseconds, diagnosticCaptureRender.Count,
            diagnosticCaptureMapMilliseconds, diagnosticCaptureMapCalls, diagnosticCaptureIdentityFailures,
            SurfaceWorkDiagnosticText.FormatQueue("captureAdmitted", diagnosticCaptureQueue, Environment.TickCount64));
        if (captureVoxelShader != null)
            capi.Logger.Notification("[VGE] Surface cache work: {0}", SurfaceWorkDiagnosticText.Format(
                SurfaceWorkStage.Capture, captureVoxelShader.Diagnostics.Snapshot(SurfaceWorkStage.Capture)));
    }

    /// <summary>Clears world-specific timing and pending identities on leave.</summary>
    private void ResetCaptureMeasurements()
    {
        diagnosticCaptureRender.Clear(); diagnosticCaptureQueue.Clear();
        diagnosticCaptureMapMilliseconds = 0;
        diagnosticCaptureMapCalls = diagnosticCaptureIdentityFailures = 0;
    }
    #endregion
}
