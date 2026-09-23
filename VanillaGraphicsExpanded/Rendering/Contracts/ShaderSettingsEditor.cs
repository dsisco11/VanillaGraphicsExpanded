using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Collects option edits for one atomic shader settings publication. Do not retain beyond its callback.</summary>
internal sealed class ShaderSettingsEditor
{
    private readonly GpuShaderContract contract;
    private bool failed;
    private readonly Dictionary<string, string?> values;

    #region Batch editing
    /// <summary>Copies requested values once; structural assignment validation is deferred until completion.</summary>
    internal ShaderSettingsEditor(ShaderSettings settings)
    {
        contract = settings.Contract;
        values = settings.Values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Canonical, StringComparer.Ordinal);
    }

    /// <summary>Validates a typed key and scalar without projecting an intermediate shader variant.</summary>
    internal void Set<T>(ShaderOption<T> option, T value) where T : struct
    {
        try
        {
            if (!contract.FindOption(option.Name).Equivalent(option))
                throw new ArgumentException($"Program '{contract.Identity}' has an incompatible typed key '{option.Name}'.");
            Set(option.Name, ShaderScalar.From(value).Canonical);
        }
        catch { failed = true; throw; }
    }

    /// <summary>Normalizes a declared name or alias; null restores its declaration default. Later writes win.</summary>
    internal void Set(string name, string? value)
    {
        try
        {
            var option = contract.FindOption(name);
            values[option.Name] = value == null ? option.Default.Canonical : option.Parse(value).Canonical;
        }
        catch { failed = true; throw; }
    }

    /// <summary>Validates the complete structural assignment and creates the single immutable snapshot.</summary>
    internal ShaderSettings Complete()
    {
        if (failed) throw new InvalidOperationException("A shader option edit failed; the batch was discarded.");
        return new(contract, values);
    }
    #endregion
}
