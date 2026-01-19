using System;
using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal static class MaterialAtlasDiskCacheIoQueue
{
    // Hard cap to prevent unbounded Task.Run IO accumulation.
    private const int MaxConcurrentIo = 2;

    private static readonly SemaphoreSlim Gate = new(MaxConcurrentIo, MaxConcurrentIo);

    public static bool TryQueue(Action work)
    {
        if (work is null) throw new ArgumentNullException(nameof(work));

        if (!Gate.Wait(0))
        {
            return false;
        }

        _ = Task.Run(() =>
        {
            try
            {
                work();
            }
            catch
            {
                // Best-effort.
            }
            finally
            {
                Gate.Release();
            }
        });

        return true;
    }
}
