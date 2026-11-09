namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Identifies a declared projection within its owning numerical domain.</summary>
internal readonly record struct GpuImageProjection(GpuObservationDomain Definition, int Choice)
{
    #region Internal API
    /// <summary>Identifies the independently authored domain asset.</summary>
    internal string Domain => Definition?.Name ?? string.Empty;
    /// <summary>Uses the declared source footprint rather than caller-modifiable mask fields.</summary>
    internal uint Inputs => Definition.RequiredInputs(Choice);
    /// <summary>Declares that the domain authors a serial scalar rather than a per-texel projection.</summary>
    internal bool IsOrderedScalar => Definition?.IsOrderedScalar(Choice) == true;
    /// <summary>Proves membership in the immutable domain-owned declaration set.</summary>
    internal bool IsDeclared => Definition?.Contains(Choice) == true;
    #endregion
}
