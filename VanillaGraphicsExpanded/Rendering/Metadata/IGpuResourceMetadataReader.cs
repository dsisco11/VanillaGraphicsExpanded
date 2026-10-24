namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Captures proven native facts once on the manager's context thread without changing error handling.</summary>
internal interface IGpuResourceMetadataReader<TMetadata> where TMetadata : struct
{
    /// <summary>Validates a realized native object of the expected resource kind, rather than a reserved name.</summary>
    bool IsResource(nint id);

    /// <summary>Validates native identity and reads a complete canonical candidate, returning false for invalid,
    /// deletion-pending or unavailable resources. Callers need no separate IsResource precheck.</summary>
    bool TryRead(nint id, out TMetadata metadata);
}
