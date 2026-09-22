using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Projects immutable registry declarations into the existing builder and stage-loader API.</summary>
internal sealed class LegacyShaderStageContract
{
    /// <summary>A compatibility view of one typed numeric declaration.</summary>
    public sealed record Specialization(int Id, string Name, string Type, string Default);
    private readonly ShaderStageContract stage;
    public IReadOnlyDictionary<string, string> StructuralDefaults { get; }

    #region Registry adaptation
    /// <summary>Adapts a known declaration without maintaining another stage or option catalog.</summary>
    public LegacyShaderStageContract(ShaderStageContract stage)
    {
        this.stage = stage;
        StructuralDefaults = stage.Structural.ToDictionary(o => o.Name, o => o.Default.Canonical, StringComparer.Ordinal);
    }

    /// <summary>Normalizes known options through typed declarations for the existing numeric loader.</summary>
    public static string Value(string name, string fallback, IReadOnlyDictionary<string, string?>? overrides)
    {
        var option = GpuShaderContracts.Registry.Programs.Values.SelectMany(p => p.Options)
            .FirstOrDefault(o => o.Name == name || o.Aliases.Contains(name))
            ?? throw new ArgumentException($"Unknown shader option '{name}'.");
        return Read(option, overrides, option.Parse(fallback)).Canonical;
    }

    /// <summary>Accepts equivalent aliases and rejects conflicting values while ignoring other stage inputs.</summary>
    private static ShaderScalar Read(ShaderOption option, IReadOnlyDictionary<string, string?>? values, ShaderScalar fallback)
    {
        ShaderScalar? selected = null;
        foreach (string name in option.Aliases.Prepend(option.Name))
        {
            if (values == null || !values.TryGetValue(name, out var text) || text == null) continue;
            var value = option.Parse(text);
            if (selected.HasValue && selected.Value != value)
                throw new ArgumentException($"Option '{option.Name}' has conflicting alias values.");
            selected = value;
        }
        return selected ?? fallback;
    }

    /// <summary>Projects only this stage's inputs; program-wide validation belongs to its settings owner.</summary>
    private Dictionary<string, ShaderScalar> Values(IReadOnlyDictionary<string, string?>? overrides) =>
        stage.Structural.Concat(stage.Specializations.Select(s => s.Option))
            .ToDictionary(o => o.Name, o => Read(o, overrides, o.Default), StringComparer.Ordinal);

    /// <summary>Uses typed canonical stage identity for the transitional callers.</summary>
    public string VariantKey(IReadOnlyDictionary<string, string?>? overrides) =>
        new ShaderStageSelection(stage, Values(overrides)).Key;

    /// <summary>Selects the immutable stage's default or keyed output path.</summary>
    public string BinaryPath(string source, IReadOnlyDictionary<string, string?>? overrides)
    {
        if (source != stage.Identity) throw new ArgumentException($"Stage '{stage.Identity}' cannot load '{source}'.");
        return new ShaderStageSelection(stage, Values(overrides)).BinaryPath;
    }

    /// <summary>Enumerates the stage's declared finite domains for the transitional build loop.</summary>
    public IEnumerable<Dictionary<string, string?>> Variants()
    {
        IEnumerable<Dictionary<string, string?>> rows = [new(StringComparer.Ordinal)];
        foreach (var option in stage.Structural)
            rows = rows.SelectMany(row => option.Domain!.Select(value =>
                new Dictionary<string, string?>(row, StringComparer.Ordinal) { [option.Name] = value.Canonical })).ToArray();
        return rows;
    }

    /// <summary>Evaluates the registry condition shared with typed runtime selection.</summary>
    public IEnumerable<Specialization> Constants(IReadOnlyDictionary<string, string?>? overrides)
    {
        var values = Values(overrides);
        return stage.Specializations.Where(s => s.Condition?.Evaluate(values) ?? true)
            .Select(s => new Specialization((int)s.Id, s.Option.Name, s.Option.Default.GlslType, s.Option.Default.GlslLiteral));
    }

    /// <summary>Retains the existing deterministic compiler scratch-file naming.</summary>
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    #endregion
}
