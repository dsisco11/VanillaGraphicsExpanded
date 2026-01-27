using System;
using System.Threading;

namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

internal interface IArtifactReservationPool
{
    bool TryAcquire(out ArtifactReservationToken token);
}

internal readonly struct ArtifactReservationToken : IDisposable
{
    private readonly Action? release;

    public ArtifactReservationToken(Action release)
    {
        this.release = release;
    }

    public void Dispose()
    {
        release?.Invoke();
    }
}

internal sealed class ArtifactReservationPool : IArtifactReservationPool
{
    private readonly SemaphoreSlim semaphore;

    public ArtifactReservationPool(int capacity)
    {
        semaphore = new SemaphoreSlim(Math.Max(0, capacity), Math.Max(0, capacity));
    }

    public bool TryAcquire(out ArtifactReservationToken token)
    {
        if (!semaphore.Wait(0))
        {
            token = default;
            return false;
        }

        token = new ArtifactReservationToken(() => semaphore.Release());
        return true;
    }
}
