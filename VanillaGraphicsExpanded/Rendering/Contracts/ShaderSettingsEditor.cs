using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Collects option edits for one atomic shader settings publication. Do not retain beyond its callback.</summary>
internal sealed class ShaderSettingsEditor
{
    private readonly GpuShaderContract contract;
    private readonly ShaderSettings original;
    private bool failed;
    private Dictionary<string, string?>? values;

    #region Batch editing
    /// <summary>Borrows the immutable requested snapshot until an edit changes a normalized value.</summary>
    internal ShaderSettingsEditor(ShaderSettings settings)
    {
        contract = settings.Contract;
        original = settings;
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
            string canonical = value == null ? option.Default.Canonical : option.Parse(value).Canonical;
            // Validate even unchanged writes, then compare against pending edits rather than installed state.
            string? prior = values == null ? original.Values[option.Name].Canonical : values[option.Name];
            if (prior == canonical) return;
            values ??= original.Values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Canonical, StringComparer.Ordinal);
            values[option.Name] = canonical;
        }
        catch { failed = true; throw; }
    }

    /// <summary>Retains an unchanged snapshot or validates and publishes the complete edited assignment.</summary>
    internal ShaderSettings Complete()
    {
        if (failed) throw new InvalidOperationException("A shader option edit failed; the batch was discarded.");
        // A batch may temporarily change a value and restore it, including through aliases or nested edits.
        if (values == null || values.All(pair => pair.Value == original.Values[pair.Key].Canonical)) return original;
        return new(contract, values);
    }
    #endregion
}
