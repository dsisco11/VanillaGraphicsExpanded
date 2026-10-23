namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Captures proven native facts once on the manager's context thread without changing error handling.</summary>
internal interface IGpuResourceMetadataReader<TMetadata> where TMetadata : struct
{
    /// <summary>Validates a realized native object of the expected resource kind, rather than a reserved name.</summary>
    bool IsResource(nint id);

    /// <summary>Reads a complete canonical candidate, returning false when any represented fact is unavailable.</summary>
    bool TryRead(nint id, out TMetadata metadata);
}
