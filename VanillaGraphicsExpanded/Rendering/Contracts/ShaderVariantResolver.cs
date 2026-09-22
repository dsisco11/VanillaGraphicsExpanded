using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Validates a registry scope and resolves typed selections without asset or graphics access.</summary>
internal sealed class ShaderVariantResolver
{
    public IReadOnlyDictionary<string, GpuShaderContract> Programs { get; }
    public IReadOnlyDictionary<string, ShaderStageContract> Stages { get; }
    public IReadOnlyList<ShaderStageSelection> Binaries { get; }

    #region Registry validation
    /// <summary>Validates shared stage agreement and every output path before publication or compilation.</summary>
    public ShaderVariantResolver(IEnumerable<GpuShaderContract> programs)
    {
        var programMap = new Dictionary<string, GpuShaderContract>(StringComparer.Ordinal);
        var stageMap = new Dictionary<string, ShaderStageContract>(StringComparer.Ordinal);
        foreach (var program in programs.OrderBy(p => p.Identity, StringComparer.Ordinal))
        {
            if (!programMap.TryAdd(program.Identity, program)) throw new ArgumentException($"Duplicate program identity '{program.Identity}'.");
            foreach (var stage in program.Stages)
            {
                if (stageMap.TryGetValue(stage.Identity, out var shared) && !shared.Equivalent(stage))
                    throw new ArgumentException($"Program '{program.Identity}' conflicts with shared stage '{stage.Identity}'.");
                stageMap.TryAdd(stage.Identity, stage);
            }
        }
        Programs = new ReadOnlyDictionary<string, GpuShaderContract>(programMap);
        Stages = new ReadOnlyDictionary<string, ShaderStageContract>(stageMap);
        var binaries = new Dictionary<(string Identity, string Key), ShaderStageSelection>();
        var paths = new Dictionary<string, (string Identity, string Key)>(StringComparer.OrdinalIgnoreCase);
        foreach (var program in programMap.Values)
            foreach (var assignment in program.Assignments)
            {
                var settings = new ShaderSettings(program, assignment.ToDictionary(p => p.Key, p => (string?)p.Value.Canonical));
                foreach (var stage in program.Stages)
                {
                    // Binary enumeration carries declaration defaults for numeric inputs, even if a
                    // shared option is structural in another stage. Consumers cannot select compiler defaults.
                    var values = stage.Specializations.ToDictionary(s => s.Option.Name, s => s.Option.Default, StringComparer.Ordinal);
                    foreach (var option in stage.Structural) values.Add(option.Name, settings.Values[option.Name]);
                    var selection = new ShaderStageSelection(stageMap[stage.Identity], values);
                    var identity = (stage.Identity, selection.Key);
                    if (paths.TryGetValue(selection.BinaryPath, out var existing) && existing != identity)
                        throw new ArgumentException($"Program '{program.Identity}', stage '{stage.Identity}' output path '{selection.BinaryPath}' collides with stage '{existing.Identity}'.");
                    paths[selection.BinaryPath] = identity;
                    binaries.TryAdd(identity, selection);
                }
            }
        Binaries = Array.AsReadOnly(binaries.Values.OrderBy(s => s.Stage.Identity, StringComparer.Ordinal).ThenBy(s => s.Key, StringComparer.Ordinal).ToArray());
    }
    #endregion

    #region Selection
    /// <summary>Resolves an explicitly registered program identity.</summary>
    public GpuShaderContract FindProgram(string identity) => Programs.TryGetValue(identity, out var program) ? program :
        throw new ArgumentException($"Unknown shader program '{identity}'.");

    /// <summary>Resolves an explicitly registered stage identity.</summary>
    public ShaderStageContract FindStage(string identity) => Stages.TryGetValue(identity, out var stage) ? stage :
        throw new ArgumentException($"Unknown shader stage '{identity}'.");

    /// <summary>Projects all stages from one immutable settings snapshot.</summary>
    public IReadOnlyList<ShaderStageSelection> Resolve(ShaderSettings settings)
    {
        var program = FindProgram(settings.Contract.Identity);
        if (!ReferenceEquals(program, settings.Contract))
            throw new ArgumentException($"Settings for program '{program.Identity}' belong to a different contract declaration.");
        return ResolveStages(settings);
    }
    /// <summary>Projects an explicit validated contract snapshot, including isolated fixture scopes.</summary>
    public static IReadOnlyList<ShaderStageSelection> ResolveStages(ShaderSettings settings) =>
        Array.AsReadOnly(settings.Contract.Stages.Select(stage => new ShaderStageSelection(stage, settings.Values)).ToArray());
    #endregion
}
