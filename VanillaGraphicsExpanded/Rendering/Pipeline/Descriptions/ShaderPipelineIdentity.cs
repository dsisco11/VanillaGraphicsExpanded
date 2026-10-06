using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Structural graphics shader identity projected from the existing immutable load plan.</summary>
internal sealed record ShaderPipelineIdentity
{
    private readonly int hashCode;
    public string AssetDomain { get; }
    public IReadOnlyList<ShaderStageSelection> Stages { get; }

    #region Public API
    /// <summary>Retains immutable shader selections in canonical stage order and caches their structural hash.</summary>
    public ShaderPipelineIdentity(string assetDomain, ShaderLoadPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetDomain);
        ArgumentNullException.ThrowIfNull(plan);
        AssetDomain = assetDomain;
        Stages = Array.AsReadOnly(plan.Stages.OrderBy(s => s.Stage.Kind).ToArray());
        // A compute selection must never be accepted merely because it has a valid resource layout.
        if (Stages.Any(s => s.Stage.Kind == ShaderStageKind.Compute) || !Stages.Any(s => s.Stage.Kind == ShaderStageKind.Vertex))
            throw new ArgumentException("A graphics identity requires a vertex stage and excludes compute.", nameof(plan));
        var hash = new HashCode();
        hash.Add(AssetDomain, StringComparer.Ordinal);
        foreach (var stage in Stages) hash.Add(ShaderStageSelectionComparer.Instance.GetHashCode(stage));
        hashCode = hash.ToHashCode();
    }

    /// <summary>Uses cached hashes only for rejection; exact effective-input comparison remains authoritative.</summary>
    public bool Equals(ShaderPipelineIdentity? other) => ReferenceEquals(this, other)
        || other is not null && hashCode == other.hashCode && AssetDomain == other.AssetDomain
        && Stages.SequenceEqual(other.Stages, ShaderStageSelectionComparer.Instance);

    /// <inheritdoc />
    public override int GetHashCode() => hashCode;
    #endregion
}
