using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Engine-independent stage kinds, including optional graphics pipeline stages.</summary>
internal enum ShaderStageKind { Vertex, Fragment, Geometry, TessellationControl, TessellationEvaluation, Compute }

/// <summary>An immutable stage identity, source, binding layout and explicit configuration uses.</summary>
internal sealed class ShaderStageContract
{
    public string Identity { get; }
    public string Source { get; }
    public string BinaryAsset { get; }
    public string EntryPoint { get; }
    public ShaderStageKind Kind { get; }
    public GpuBindingContract Bindings { get; }
    public IReadOnlyList<ShaderOption> Structural { get; }
    public IReadOnlyList<ShaderSpecialization> Specializations { get; }
    public IReadOnlyDictionary<string, ShaderScalar> FixedDefines { get; }

    #region Declaration
    /// <summary>Snapshots all inputs, validating the stage before publication.</summary>
    public ShaderStageContract(string identity, string source, ShaderStageKind kind, GpuBindingContract bindings,
        IEnumerable<ShaderOption>? structural = null, IEnumerable<ShaderSpecialization>? specializations = null,
        IReadOnlyDictionary<string, ShaderScalar>? fixedDefines = null, string entryPoint = "main", string? binaryAsset = null)
    {
        ShaderContractNames.ValidatePath(identity); ShaderContractNames.ValidatePath(source);
        ShaderContractNames.ValidatePath(binaryAsset ?? identity); ShaderContractNames.ValidateIdentifier(entryPoint);
        if (!Enum.IsDefined(typeof(ShaderStageKind), kind)) throw new ArgumentException($"Stage '{identity}' has invalid type '{kind}'.");
        Identity = identity; Source = source; Kind = kind; EntryPoint = entryPoint; BinaryAsset = binaryAsset ?? identity;
        Bindings = bindings.Snapshot();
        Structural = Array.AsReadOnly((structural ?? []).OrderBy(o => o.Name, StringComparer.Ordinal).ToArray());
        Specializations = Array.AsReadOnly((specializations ?? []).OrderBy(s => s.Id).ToArray());
        FixedDefines = new ReadOnlyDictionary<string, ShaderScalar>((fixedDefines ?? new Dictionary<string, ShaderScalar>()).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in Structural.Concat(Specializations.Select(s => s.Option)))
            foreach (string name in option.Aliases.Prepend(option.Name))
                if (!names.Add(name)) throw new ArgumentException($"Stage '{identity}' repeats option/name '{name}'.");
        foreach (var option in Structural)
            if (option.Domain == null || option.ScalarType == ShaderScalarType.Float)
                throw new ArgumentException($"Stage '{identity}' structural option '{option.Name}' needs a finite Boolean/integer/enum domain.");
        foreach (var pair in FixedDefines)
        {
            ShaderContractNames.ValidateIdentifier(pair.Key);
            if (!names.Add(pair.Key)) throw new ArgumentException($"Stage '{identity}' fixed name '{pair.Key}' conflicts with a configurable option.");
        }
        if (Specializations.Select(s => s.Id).Distinct().Count() != Specializations.Count)
            throw new ArgumentException($"Stage '{identity}' repeats a specialization ID.");
        foreach (var specialization in Specializations) specialization.Condition?.Validate(Structural, identity);
    }

    /// <summary>Compares immutable definitions, allowing equal declarations from independent owners.</summary>
    internal bool Equivalent(ShaderStageContract other) => Identity == other.Identity && Source == other.Source &&
        BinaryAsset == other.BinaryAsset && EntryPoint == other.EntryPoint && Kind == other.Kind && Bindings.Equivalent(other.Bindings) &&
        Structural.Count == other.Structural.Count && Structural.Zip(other.Structural, (left, right) => left.Equivalent(right)).All(equal => equal) &&
        Specializations.Count == other.Specializations.Count && Specializations.Zip(other.Specializations, (left, right) => left.Equivalent(right)).All(equal => equal) &&
        FixedDefines.Count == other.FixedDefines.Count && FixedDefines.All(p => other.FixedDefines.TryGetValue(p.Key, out var v) && p.Value == v);
    #endregion
}
