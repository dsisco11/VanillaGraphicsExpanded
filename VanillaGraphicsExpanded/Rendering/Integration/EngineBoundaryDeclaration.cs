using System;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Rendering.Integration;

/// <summary>Names an engine interruption and unions its pipeline, dynamic and documented helper effects.</summary>
internal sealed class EngineBoundaryDeclaration
{
    internal string Name { get; }
    internal PipelineStateCoverage Coverage { get; }

    #region Public API
    /// <summary>Freezes the complete preservation contract before optional rendering begins.</summary>
    internal EngineBoundaryDeclaration(string name, params PipelineStateCoverage[] effects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(effects);
        Name = name;
        var coverage = PipelineStateCoverage.Empty;
        foreach (var effect in effects) coverage = coverage.Union(effect);
        Coverage = coverage;
    }
    #endregion
}
