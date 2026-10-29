namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Captures proven native facts once on the manager's context thread without changing error handling.</summary>
internal interface IGpuResourceMetadataReader<TMetadata> where TMetadata : struct
{
    /// <summary>Validates native identity and reads a complete canonical candidate, returning false for invalid,
    /// deletion-pending or unavailable resources. Callers need no separate native validity probe.</summary>
    bool TryRead(nint id, out TMetadata metadata);
}
