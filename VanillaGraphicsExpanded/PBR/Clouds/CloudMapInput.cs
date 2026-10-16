namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>A same-frame read-only borrow. Consumers must validate it again before use; no ownership of native GL names is transferred.</summary>
internal readonly record struct CloudMapInput(object Owner, long World, long Allocation, long Upload,
    long Shader, long Generation, uint Frame, CloudMapStorage Storage, CloudPlacement Placement)
{
    /// <summary>Matches the current atmospheric distance convention at the input boundary.</summary>
    internal const float MetresPerWorldUnit = 1;
}

