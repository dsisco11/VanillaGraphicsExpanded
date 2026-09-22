using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Projects one immutable settings snapshot into transient, coherent stage load selections.</summary>
internal sealed class ShaderLoadPlan
{
    public ShaderSettings Settings { get; }
    public IReadOnlyList<ShaderStageSelection> Stages { get; }

    #region Projection and comparison
    /// <summary>Resolves all stage inputs before asset reads or GPU allocation can observe later setting writes.</summary>
    public ShaderLoadPlan(ShaderSettings settings)
    {
        Settings = settings;
        Stages = ShaderVariantResolver.ResolveStages(settings);
    }

    /// <summary>Compares effective inputs, excluding retained inactive settings and alias spellings.</summary>
    public bool SameInputs(ShaderLoadPlan other) => ReferenceEquals(Settings.Contract, other.Settings.Contract) &&
        Stages.Count == other.Stages.Count && Stages.Zip(other.Stages).All(p => p.First.SameInputs(p.Second));
    #endregion
}
