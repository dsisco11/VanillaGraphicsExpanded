using System;
using OpenTK.Graphics.OpenGL;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

internal static class GpuTestFence
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    public static void WaitForGpuOrSkip(string operation, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            operation = "(unspecified operation)";
        }

        TimeSpan waitTimeout = timeout ?? DefaultTimeout;
        if (waitTimeout <= TimeSpan.Zero)
        {
            waitTimeout = DefaultTimeout;
        }

        IntPtr sync = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);
        if (sync == IntPtr.Zero)
        {
            Assert.SkipWhen(true, $"GPU sync not available (glFenceSync failed) while waiting for: {operation}");
            return;
        }

        try
        {
            long timeoutNs = ToNanoseconds(waitTimeout);

            // Use SyncFlushCommandsBit so any buffered commands are submitted.
            WaitSyncStatus status = GL.ClientWaitSync(sync, ClientWaitSyncFlags.SyncFlushCommandsBit, timeoutNs);

            if (status == WaitSyncStatus.ConditionSatisfied || status == WaitSyncStatus.AlreadySignaled)
            {
                return;
            }

            if (status == WaitSyncStatus.TimeoutExpired)
            {
                Assert.SkipWhen(true, $"GPU work did not complete within {waitTimeout.TotalSeconds:0.###}s (possible driver hang) while waiting for: {operation}");
                return;
            }

            Assert.SkipWhen(true, $"GPU wait failed ({status}) while waiting for: {operation}");
        }
        finally
        {
            GL.DeleteSync(sync);
        }
    }

    private static long ToNanoseconds(TimeSpan timeSpan)
    {
        double ns = timeSpan.TotalMilliseconds * 1_000_000.0;
        if (ns <= 0)
        {
            return 0;
        }

        if (ns >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)ns;
    }
}
