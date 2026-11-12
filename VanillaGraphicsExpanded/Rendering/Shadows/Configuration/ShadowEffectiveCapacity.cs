namespace VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

/// <summary>Requested finite layout and its charged fixed/scratch reservation before any resource creation.</summary>
internal sealed record ShadowEffectiveCapacity(long RequestedBytes,long FixedBytes,long DrawingReserveBytes,
    long TileCapacity,int CandidateHeaders,int WorldLookupSlots,int DemandRequests,int CommandSlots,int Views)
{
    /// <summary>Remaining bytes include all live and in-flight allocations; driver-private overhead is untracked.</summary>
    internal long AvailableTransientBytes=>RequestedBytes-FixedBytes;
}
