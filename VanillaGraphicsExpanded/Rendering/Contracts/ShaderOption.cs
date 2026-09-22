using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Immutable metadata common to heterogeneous typed shader options.</summary>
internal abstract class ShaderOption
{
    public string Name { get; }
    public Type ValueType { get; }
    public ShaderScalarType ScalarType { get; }
    public ShaderScalar Default { get; }
    public IReadOnlyList<string> Aliases { get; }
    public IReadOnlyList<ShaderScalar>? Domain { get; }
    public ShaderScalar? Minimum { get; }
    public ShaderScalar? Maximum { get; }

    #region Declaration and validation
    /// <summary>Copies domains/aliases and validates defaults before the option can be published.</summary>
    protected ShaderOption(string name, Type valueType, ShaderScalar fallback, IEnumerable<ShaderScalar>? domain,
        ShaderScalar? minimum, ShaderScalar? maximum, IEnumerable<string>? aliases)
    {
        ShaderContractNames.ValidateIdentifier(name);
        Name = name; ValueType = valueType; ScalarType = fallback.Type; Default = fallback;
        var names = (aliases ?? []).ToArray();
        foreach (string alias in names) ShaderContractNames.ValidateIdentifier(alias);
        if (names.Append(name).Distinct(StringComparer.Ordinal).Count() != names.Length + 1)
            throw new ArgumentException($"Option '{name}' has duplicate aliases.");
        Aliases = Array.AsReadOnly(names);
        var values = domain?.ToArray();
        if (values != null && (values.Length == 0 || values.Distinct().Count() != values.Length))
            throw new ArgumentException($"Option '{name}' requires a nonempty, duplicate-free domain.");
        Domain = values == null ? null : Array.AsReadOnly(values);
        Minimum = minimum; Maximum = maximum;
        if (minimum.HasValue && maximum.HasValue && minimum.Value.CompareTo(maximum.Value) > 0)
            throw new ArgumentException($"Option '{name}' has an inverted range.");
        if (values != null) foreach (var value in values) Validate(value);
        Validate(fallback);
    }

    /// <summary>Checks the declared finite domain and optional scalar bounds.</summary>
    public ShaderScalar Validate(ShaderScalar value)
    {
        if (value.Type != ScalarType || Domain != null && !Domain.Contains(value) ||
            Minimum.HasValue && value.CompareTo(Minimum.Value) < 0 || Maximum.HasValue && value.CompareTo(Maximum.Value) > 0)
            throw new ArgumentException($"Option '{Name}' rejects value '{value.Canonical}' ({value.Type}); expected {ScalarType} in its declared domain/range.");
        return value;
    }

    /// <summary>Normalizes a compatibility value using this option's declared scalar type.</summary>
    public ShaderScalar Parse(string value)
    {
        try { return Validate(ShaderScalar.Parse(ScalarType, value)); }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        { throw new ArgumentException($"Option '{Name}' rejects value '{value}': {e.Message}", e); }
    }

    /// <summary>Compares declarations when consumers explicitly share a canonical option.</summary>
    internal bool Equivalent(ShaderOption other) => Name == other.Name && ValueType == other.ValueType &&
        Default == other.Default && Minimum == other.Minimum && Maximum == other.Maximum &&
        Aliases.Order(StringComparer.Ordinal).SequenceEqual(other.Aliases.Order(StringComparer.Ordinal)) &&
        (Domain == null ? other.Domain == null : other.Domain != null && Domain.ToHashSet().SetEquals(other.Domain));
    #endregion
}

/// <summary>A typed option key with immutable defaults, allowed values, bounds and compatibility aliases.</summary>
internal sealed class ShaderOption<T> : ShaderOption where T : struct
{
    #region Typed declaration
    /// <summary>Declares one scalar option; Boolean options have their natural two-value domain.</summary>
    public ShaderOption(string name, T defaultValue, IEnumerable<T>? domain = null, T? minimum = null,
        T? maximum = null, IEnumerable<string>? aliases = null)
        : base(name, typeof(T), ShaderScalar.From(defaultValue), GetDomain(domain),
            minimum.HasValue ? ShaderScalar.From(minimum.Value) : null,
            maximum.HasValue ? ShaderScalar.From(maximum.Value) : null, aliases) { }

    /// <summary>Uses explicit enum/integer domains and only supplies the intrinsic Boolean domain automatically.</summary>
    private static IEnumerable<ShaderScalar>? GetDomain(IEnumerable<T>? domain) => domain?.Select(ShaderScalar.From) ??
        (typeof(T) == typeof(bool) ? [ShaderScalar.From(false), ShaderScalar.From(true)] : null);
    #endregion
}
