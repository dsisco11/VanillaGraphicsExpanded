using System;
using System.Threading;

namespace VanillaGraphicsExpanded.PBR.Materials.ArtifactSystem;

internal sealed class MaterialAtlasArtifactBuildTracker
{
    private int generationId;
    private int remaining;

    public int GenerationId => Volatile.Read(ref generationId);

    public int Remaining => Volatile.Read(ref remaining);

    public bool IsComplete => Remaining <= 0;

    public void Begin(int generationId, int totalWorkItems)
    {
        if (generationId <= 0) throw new ArgumentOutOfRangeException(nameof(generationId));
        if (totalWorkItems < 0) throw new ArgumentOutOfRangeException(nameof(totalWorkItems));

        Interlocked.Exchange(ref this.generationId, generationId);
        Interlocked.Exchange(ref remaining, totalWorkItems);
    }

    public void CompleteOne(int generationId)
    {
        if (generationId != Volatile.Read(ref this.generationId))
        {
            return;
        }

        Interlocked.Decrement(ref remaining);
    }
}
