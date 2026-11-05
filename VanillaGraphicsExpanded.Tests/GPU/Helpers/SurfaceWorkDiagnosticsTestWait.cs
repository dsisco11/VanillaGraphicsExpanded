using System.Diagnostics;
using System.Threading;
using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Waits for admitted collector samples to become CPU-readable after the GPU completion fence.</summary>
internal static class SurfaceWorkDiagnosticsTestWait
{
    private static readonly TimeSpan QueryAvailabilityTimeout=TimeSpan.FromSeconds(2);

    #region Public API
    /// <summary>Polls completed timestamp pairs to a bounded deadline without changing production collection behavior.</summary>
    internal static void Drain(SurfaceWorkDiagnostics diagnostics,string operation)
    {
        GpuTestFence.WaitForGpuOrSkip(operation);
        long started=Stopwatch.GetTimestamp();
        do
        {
            diagnostics.Poll();
            if(diagnostics.PendingCount==0)return;
            Thread.Yield();
        }
        while(Stopwatch.GetElapsedTime(started)<QueryAvailabilityTimeout);
        Assert.True(false,$"Diagnostic timestamp and counter results remained unavailable after GPU completion: {operation}; pending={diagnostics.PendingCount}.");
    }
    #endregion
}
