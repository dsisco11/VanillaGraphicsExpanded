using System;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Preserves original admission validation through lighting retries after resident payload ownership retires.</summary>
internal sealed class WorldProbeCommitIdentity
{
    private readonly Func<bool> validate;
    public bool IsCurrent => validate();

    /// <summary>Captures render-thread checks without retaining ownership of a GPU allocation.</summary>
    public WorldProbeCommitIdentity(Func<bool> validate) { this.validate = validate; }
}
