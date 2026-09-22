using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Preserves the existing builder/loader configuration API until their typed-contract migration.</summary>
internal sealed class LegacyShaderStageContract
{
    /// <summary>A numeric constant and the structural choices under which its declaration is emitted.</summary>
    public sealed record Specialization(int Id, string Name, string Type, string Default,
        Func<IReadOnlyDictionary<string, string?>?, bool>? Enabled = null);
    public Dictionary<string, string> StructuralDefaults { get; } = new(StringComparer.Ordinal);
    public List<Specialization> Specializations { get; } = [];

    #region Variant selection
    /// <summary>Normalizes a setting, retaining the supported legacy AO alias.</summary>
    public static string Value(string name, string fallback, IReadOnlyDictionary<string, string?>? overrides)
    {
        string? value = null;
        overrides?.TryGetValue(name, out value);
        if (value == null && name == "VGE_LUMON_ENABLE_SHORT_RANGE_AO")
            overrides?.TryGetValue("VGE_LUMON_ENABLE_BENT_NORMAL", out value);
        return double.Parse(value ?? fallback, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Produces the same finite configuration identity in the builder and loader.</summary>
    public string VariantKey(IReadOnlyDictionary<string, string?>? overrides)
    {
        return string.Join(";", StructuralDefaults.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
        {
            string value = Value(p.Key, p.Value, overrides);
            if (value is not ("0" or "1")) throw new ArgumentOutOfRangeException(p.Key, "Expected a binary shader option.");
            return p.Key + "=" + value;
        }));
    }

    /// <summary>Addresses the default binary directly and other finite choices by their canonical key.</summary>
    public string BinaryPath(string source, IReadOnlyDictionary<string, string?>? overrides)
    {
        string key = VariantKey(overrides);
        return key == VariantKey(null) ? source + ".spv" : "variants/" + source + "/" + Hash(key) + ".spv";
    }

    /// <summary>Enumerates the declared preprocessing configurations for offline compilation and tests.</summary>
    public IEnumerable<Dictionary<string, string?>> Variants()
    {
        IEnumerable<Dictionary<string, string?>> rows = [new(StringComparer.Ordinal)];
        foreach (string name in StructuralDefaults.Keys.Order(StringComparer.Ordinal))
            rows = rows.SelectMany(row => new[] { "0", "1" }.Select(value =>
                new Dictionary<string, string?>(row, StringComparer.Ordinal) { [name] = value })).ToArray();
        return rows;
    }

    /// <summary>Selects only constants declared by this structural configuration.</summary>
    public IEnumerable<Specialization> Constants(IReadOnlyDictionary<string, string?>? overrides) =>
        Specializations.Where(s => s.Enabled?.Invoke(overrides) ?? true);

    /// <summary>Produces a stable filename component for a canonical variant key.</summary>
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    #endregion
}
