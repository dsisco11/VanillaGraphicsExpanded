namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal readonly record struct MaterialAtlasDiskCacheStats(
    MaterialAtlasDiskCacheStats.Payload MaterialParams,
    MaterialAtlasDiskCacheStats.Payload NormalDepth,
    long TotalEntries,
    long TotalBytes,
    long EvictedEntries,
    long EvictedBytes)
{
    internal readonly record struct Payload(long Hits, long Misses, long Stores);
}
