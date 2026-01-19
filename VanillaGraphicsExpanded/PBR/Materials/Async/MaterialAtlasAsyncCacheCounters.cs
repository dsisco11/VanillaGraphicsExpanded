using System.Threading;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal sealed class MaterialAtlasAsyncCacheCounters
{
    private long baseHits;
    private long baseMisses;
    private long overrideHits;
    private long overrideMisses;

    public void IncrementBaseHit() => Interlocked.Increment(ref baseHits);

    public void IncrementBaseMiss() => Interlocked.Increment(ref baseMisses);

    public void IncrementOverrideHit() => Interlocked.Increment(ref overrideHits);

    public void IncrementOverrideMiss() => Interlocked.Increment(ref overrideMisses);

    public Snapshot GetSnapshot()
        => new(
            BaseHits: Interlocked.Read(ref baseHits),
            BaseMisses: Interlocked.Read(ref baseMisses),
            OverrideHits: Interlocked.Read(ref overrideHits),
            OverrideMisses: Interlocked.Read(ref overrideMisses));

    internal readonly record struct Snapshot(long BaseHits, long BaseMisses, long OverrideHits, long OverrideMisses);
}
