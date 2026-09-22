using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Constructs shader-owned immutable contracts and shares compatible stage declarations.</summary>
internal static class ShaderProgramDeclaration
{
    private static readonly ConcurrentDictionary<string, ShaderStageContract> stages = new(StringComparer.Ordinal);

    #region Declaration construction
    /// <summary>Declares a graphics pair whose listed configuration uses belong to its fragment stage.</summary>
    public static GpuShaderContract Graphics(string identity, string vertex, string fragment, int budget,
        ShaderOptionGroup[]? groups = null, ShaderSpecialization[]? constants = null)
    {
        groups ??= [];
        constants ??= [];
        // Existing graphics programs configure only fragments. Explicit numeric declarations
        // distinguish specializations from the remaining finite structural group members.
        var structural = groups.SelectMany(g => g.Options).Where(o => !constants.Any(s => s.Option.Name == o.Name)).DistinctBy(o => o.Name).ToArray();
        return new(identity,
            [Stage(vertex, ShaderStageKind.Vertex), Stage(fragment, ShaderStageKind.Fragment, structural, constants)],
            budget, groups: groups);
    }

    /// <summary>Declares an explicitly unconfigured compute program.</summary>
    public static GpuShaderContract Compute(string identity, string source) =>
        new(identity, [Stage(source, ShaderStageKind.Compute)], 1);

    /// <summary>Declares an alternate fixture vertex using the original shader's fragment and settings.</summary>
    public static GpuShaderContract Pair(string identity, string vertex, GpuShaderContract original) =>
        new(identity, [Stage(vertex, ShaderStageKind.Vertex), original.Stages.Single(s => s.Kind == ShaderStageKind.Fragment)],
            original.VariantBudget, groups: original.Groups, options: original.Options);

    /// <summary>Shares equal immutable stages independently of registry construction or owner initialization order.</summary>
    private static ShaderStageContract Stage(string source, ShaderStageKind kind,
        IEnumerable<ShaderOption>? structural = null, IEnumerable<ShaderSpecialization>? constants = null)
    {
        var declared = new ShaderStageContract(source, source, kind,
            GpuShaderContracts.DeclareBindings(source[..source.LastIndexOf('.')]), structural, constants,
            new Dictionary<string, ShaderScalar> { ["VGE_SPIRV_BUILD"] = ShaderScalar.From(1) });
        var shared = stages.GetOrAdd(source, declared);
        if (!shared.Equivalent(declared)) throw new ArgumentException($"Conflicting shared stage '{source}'.");
        return shared;
    }
    #endregion
}
