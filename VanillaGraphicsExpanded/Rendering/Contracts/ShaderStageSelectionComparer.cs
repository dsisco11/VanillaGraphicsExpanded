using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Compares effective shader inputs and interfaces without comparing unused option declarations or live executables.</summary>
internal sealed class ShaderStageSelectionComparer : IEqualityComparer<ShaderStageSelection>
{
    public static ShaderStageSelectionComparer Instance { get; } = new();

    #region Public API
    /// <summary>Compares selected inputs across independent immutable contract owners.</summary>
    public bool Equals(ShaderStageSelection? x, ShaderStageSelection? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        var a = x.Stage;
        var b = y.Stage;
        // Preserve effective-input semantics: declaration domains and inactive specializations are not pipeline state.
        return a.Kind == b.Kind && a.Identity == b.Identity && a.Source == b.Source
            && a.EntryPoint == b.EntryPoint && x.BinaryPath == y.BinaryPath && x.Key == y.Key
            && x.Specializations.SequenceEqual(y.Specializations)
            && a.FixedDefines.Count == b.FixedDefines.Count
            && a.FixedDefines.All(p => b.FixedDefines.TryGetValue(p.Key, out var value) && p.Value == value)
            && a.Bindings.Equivalent(b.Bindings);
    }

    /// <summary>Hashes the same effective inputs as equality without allocating copied contract collections.</summary>
    public int GetHashCode(ShaderStageSelection value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var stage = value.Stage;
        var hash = new HashCode();
        hash.Add(stage.Kind);
        hash.Add(stage.Identity, StringComparer.Ordinal);
        hash.Add(stage.Source, StringComparer.Ordinal);
        hash.Add(stage.EntryPoint, StringComparer.Ordinal);
        hash.Add(value.BinaryPath, StringComparer.Ordinal);
        hash.Add(value.Key, StringComparer.Ordinal);
        foreach (var argument in value.Specializations) hash.Add(argument);
        // Map enumeration order must not affect the hash of equivalent declarations.
        int defines = 0;
        foreach (var pair in stage.FixedDefines)
            defines = unchecked(defines + HashCode.Combine(StringComparer.Ordinal.GetHashCode(pair.Key), pair.Value));
        hash.Add(stage.FixedDefines.Count);
        hash.Add(defines);
        hash.Add(stage.Bindings.StructuralHashCode());
        return hash.ToHashCode();
    }
    #endregion
}
