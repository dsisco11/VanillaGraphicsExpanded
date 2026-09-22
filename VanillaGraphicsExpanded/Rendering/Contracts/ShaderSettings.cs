using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>An immutable, normalized snapshot of one program instance's requested settings.</summary>
internal sealed class ShaderSettings
{
    public GpuShaderContract Contract { get; }
    public IReadOnlyDictionary<string, ShaderScalar> Values { get; }

    #region Normalization and updates
    /// <summary>Normalizes aliases once and rejects conflicting inputs before projection or GPU allocation.</summary>
    public ShaderSettings(GpuShaderContract contract, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        Contract = contract;
        var selected = new Dictionary<string, ShaderScalar>(StringComparer.Ordinal);
        foreach (var pair in overrides ?? new Dictionary<string, string?>())
        {
            var option = contract.FindOption(pair.Key);
            if (pair.Value == null) continue;
            ShaderScalar value;
            try { value = option.Parse(pair.Value); }
            catch (ArgumentException e) { throw new ArgumentException($"Program '{contract.Identity}': {e.Message}", e); }
            if (selected.TryGetValue(option.Name, out var prior) && prior != value)
                throw new ArgumentException($"Program '{contract.Identity}', option '{option.Name}' has conflicting alias values '{prior.Canonical}' and '{pair.Value}'.");
            selected[option.Name] = value;
        }
        foreach (var option in contract.Options) selected.TryAdd(option.Name, option.Default);
        Values = new ReadOnlyDictionary<string, ShaderScalar>(selected);
        ValidateAssignment();
    }

    /// <summary>Creates a new snapshot for a compatibility update; null restores the canonical default.</summary>
    public ShaderSettings With(string name, string? value)
    {
        var option = Contract.FindOption(name);
        var values = Values.ToDictionary(p => p.Key, p => (string?)p.Value.Canonical, StringComparer.Ordinal);
        values[option.Name] = value;
        return new(Contract, values);
    }

    /// <summary>Accepts only an equivalent typed declaration from this program.</summary>
    public ShaderSettings With<T>(ShaderOption<T> option, T value) where T : struct
    {
        var declared = Contract.FindOption(option.Name);
        if (!declared.Equivalent(option)) throw new ArgumentException($"Program '{Contract.Identity}' has an incompatible typed key '{option.Name}'.");
        try { return With(option.Name, ShaderScalar.From(value).Canonical); }
        catch (ArgumentException e) { throw new ArgumentException($"Program '{Contract.Identity}', option '{option.Name}', value '{value}': {e.Message}", e); }
    }

    /// <summary>Checks membership independently from which specialization inputs happen to be active.</summary>
    private void ValidateAssignment()
    {
        if (!Contract.Assignments.Any(row => row.All(p => Values[p.Key] == p.Value)))
            throw new ArgumentException($"Program '{Contract.Identity}' does not support assignment '{ShaderAssignments.Key(Contract.Structural.Select(o => new KeyValuePair<string, ShaderScalar>(o.Name, Values[o.Name])))}'.");
    }
    #endregion
}
