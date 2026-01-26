using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal static class MaterialAtlasDiskCacheIoQueue
{
    // Option B: never drop writes; never block the render thread.
    // Implemented as a fixed-size worker pool draining a queue.
    private const int MaxConcurrentIo = 2;

    private static readonly ConcurrentQueue<Action> Pending = new();
    private static readonly SemaphoreSlim PendingSignal = new(0);
    private static int workersStarted;

    public static bool TryQueue(Action work)
    {
        if (work is null) throw new ArgumentNullException(nameof(work));

        Queue(work);
        return true;
    }

    public static void Queue(Action work)
    {
        if (work is null) throw new ArgumentNullException(nameof(work));

        EnsureWorkersStarted();

        Pending.Enqueue(work);
        PendingSignal.Release();
    }

    private static void EnsureWorkersStarted()
    {
        if (Volatile.Read(ref workersStarted) != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref workersStarted, 1) != 0)
        {
            return;
        }

        for (int i = 0; i < MaxConcurrentIo; i++)
        {
            _ = Task.Run(WorkerLoop);
        }
    }

    private static async Task WorkerLoop()
    {
        while (true)
        {
            try
            {
                await PendingSignal.WaitAsync().ConfigureAwait(false);

                if (!Pending.TryDequeue(out Action? work))
                {
                    continue;
                }

                try
                {
                    work();
                }
                catch
                {
                    // Best-effort.
                }
            }
            catch
            {
                // Best-effort: keep worker alive.
            }
        }
    }
}
