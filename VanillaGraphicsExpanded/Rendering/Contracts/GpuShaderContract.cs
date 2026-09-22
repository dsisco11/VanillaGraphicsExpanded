using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>An immutable program declaration and its finite set of supported structural assignments.</summary>
internal sealed class GpuShaderContract
{
    public string Identity { get; }
    public IReadOnlyList<ShaderStageContract> Stages { get; }
    public IReadOnlyList<ShaderOption> Options { get; }
    public IReadOnlyList<ShaderOptionGroup> Groups { get; }
    public IReadOnlyList<ShaderOption> Structural { get; }
    public IReadOnlyList<IReadOnlyDictionary<string, ShaderScalar>> Assignments { get; }
    public int VariantBudget { get; }
    private readonly Dictionary<string, ShaderOption> names = new(StringComparer.Ordinal);

    #region Declaration
    /// <summary>Checks accepted names, stage membership and the complete supported configuration set.</summary>
    public GpuShaderContract(string identity, IEnumerable<ShaderStageContract> stages, int variantBudget,
        IEnumerable<ShaderOption>? options = null, IEnumerable<ShaderOptionGroup>? groups = null,
        IEnumerable<IReadOnlyDictionary<string, string>>? supportedAssignments = null)
    {
        ShaderContractNames.ValidatePath(identity); Identity = identity; VariantBudget = variantBudget;
        if (variantBudget < 1) throw new ArgumentException($"Program '{identity}' has invalid variant budget '{variantBudget}'.");
        Stages = Array.AsReadOnly(stages.OrderBy(s => s.Kind).ToArray());
        ValidateStages();
        Groups = Array.AsReadOnly((groups ?? []).ToArray());
        if (Groups.Select(g => g.Identity).Distinct(StringComparer.Ordinal).Count() != Groups.Count)
            throw new ArgumentException($"Program '{identity}' repeats an option group.");
        var canonical = new Dictionary<string, ShaderOption>(StringComparer.Ordinal);
        foreach (var option in (options ?? []).Concat(Groups.SelectMany(g => g.Options)))
        {
            if (canonical.TryGetValue(option.Name, out var prior))
            {
                if (!prior.Equivalent(option)) throw new ArgumentException($"Program '{identity}' conflicts on option '{option.Name}'.");
                continue; // Reused groups may explicitly share the same declaration.
            }
            canonical.Add(option.Name, option);
            foreach (string name in option.Aliases.Prepend(option.Name))
                if (!names.TryAdd(name, option)) throw new ArgumentException($"Program '{identity}' repeats option/alias '{name}'.");
        }
        Options = Array.AsReadOnly(canonical.Values.OrderBy(o => o.Name, StringComparer.Ordinal).ToArray());
        foreach (var stage in Stages)
        {
            foreach (var option in stage.Structural.Concat(stage.Specializations.Select(s => s.Option)))
                if (!canonical.TryGetValue(option.Name, out var accepted) || !accepted.Equivalent(option))
                    throw new ArgumentException($"Program '{identity}', stage '{stage.Identity}' uses undeclared/incompatible option '{option.Name}'.");
            foreach (string fixedName in stage.FixedDefines.Keys)
                if (names.ContainsKey(fixedName)) throw new ArgumentException($"Program '{identity}', stage '{stage.Identity}' fixed name '{fixedName}' conflicts with a setting.");
        }
        Structural = Array.AsReadOnly(Stages.SelectMany(s => s.Structural).DistinctBy(o => o.Name).OrderBy(o => o.Name, StringComparer.Ordinal).ToArray());
        Assignments = ShaderAssignments.Create(this, supportedAssignments);
    }

    /// <summary>Rejects duplicate/invalid stage combinations without accessing source assets.</summary>
    private void ValidateStages()
    {
        var kinds = Stages.Select(s => s.Kind).ToHashSet();
        bool compute = kinds.Contains(ShaderStageKind.Compute);
        if (Stages.Count == 0 || kinds.Count != Stages.Count || Stages.Select(s => s.Identity).Distinct(StringComparer.Ordinal).Count() != Stages.Count ||
            (compute ? Stages.Count != 1 : !kinds.Contains(ShaderStageKind.Vertex) || !kinds.Contains(ShaderStageKind.Fragment)) ||
            kinds.Contains(ShaderStageKind.TessellationControl) && !kinds.Contains(ShaderStageKind.TessellationEvaluation))
            throw new ArgumentException($"Program '{Identity}' has an invalid graphics/compute stage combination.");
    }

    /// <summary>Resolves canonical names and aliases, failing explicitly for unknown settings.</summary>
    public ShaderOption FindOption(string name) => names.TryGetValue(name, out var option) ? option :
        throw new ArgumentException($"Program '{Identity}' has no option '{name}'.");
    #endregion
}
