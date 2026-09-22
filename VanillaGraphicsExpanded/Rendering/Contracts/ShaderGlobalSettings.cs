using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Projects shared graphics configuration only through explicitly accepted program groups.</summary>
internal static class ShaderGlobalSettings
{
    #region Projection
    /// <summary>Validates global names before filtering known options through a program's memberships.</summary>
    public static ShaderSettings Project(ShaderVariantResolver registry, string programIdentity,
        IReadOnlyDictionary<string, string?> values)
    {
        var program = registry.FindProgram(programIdentity);
        var known = registry.Programs.Values.SelectMany(p => p.Groups).SelectMany(g => g.Options)
            .DistinctBy(o => o.Name).ToArray();
        var accepted = program.Groups.SelectMany(g => g.Options).SelectMany(o => o.Aliases.Prepend(o.Name)).ToHashSet(StringComparer.Ordinal);
        var normalized = new Dictionary<string, ShaderScalar>(StringComparer.Ordinal);
        var projected = new Dictionary<string, string?>(StringComparer.Ordinal);
        // Validate even a known-but-irrelevant input: misspellings and conflicting aliases
        // must not disappear merely because the first selected consumer does not use them.
        foreach (var pair in values)
        {
            var option = known.FirstOrDefault(o => o.Name == pair.Key || o.Aliases.Contains(pair.Key))
                ?? throw new ArgumentException($"Unknown global shader option '{pair.Key}'.");
            if (pair.Value != null)
            {
                var scalar = option.Parse(pair.Value);
                if (normalized.TryGetValue(option.Name, out var prior) && prior != scalar)
                    throw new ArgumentException($"Global shader option '{option.Name}' has conflicting alias values.");
                normalized[option.Name] = scalar;
            }
            if (accepted.Contains(pair.Key)) projected.Add(pair.Key, pair.Value);
        }
        return new(program, projected);
    }
    #endregion
}
