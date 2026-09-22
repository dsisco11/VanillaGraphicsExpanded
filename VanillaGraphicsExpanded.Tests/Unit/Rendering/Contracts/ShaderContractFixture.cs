using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Small asset-free declarations shared by contract tests.</summary>
internal static class ShaderContractFixture
{
    public static readonly ShaderOption<bool> Enabled = new("ENABLED", false, aliases: ["LEGACY_ENABLED"]);
    public static readonly ShaderOption<int> Steps = new("STEPS", 10, minimum: 0, maximum: 100);

    #region Declarations
    /// <summary>Creates a stage with an empty binding layout unless one is supplied.</summary>
    public static ShaderStageContract Stage(string identity = "fixture.fsh", ShaderStageKind kind = ShaderStageKind.Fragment,
        IEnumerable<ShaderOption>? structural = null, IEnumerable<ShaderSpecialization>? constants = null,
        GpuBindingContract? bindings = null, string? binaryAsset = null, string? source = null,
        IReadOnlyDictionary<string, ShaderScalar>? fixedDefines = null) =>
        new(identity, source ?? identity, kind, bindings ?? new(), structural, constants, fixedDefines, binaryAsset: binaryAsset);

    /// <summary>Pairs a fragment with an explicitly declared shared vertex stage.</summary>
    public static GpuShaderContract Program(string identity = "fixture", ShaderStageContract? fragment = null,
        IEnumerable<ShaderOption>? options = null, int budget = 2,
        IEnumerable<IReadOnlyDictionary<string, string>>? assignments = null, ShaderStageContract? vertex = null,
        IEnumerable<ShaderOptionGroup>? groups = null) =>
        new(identity, [vertex ?? Stage("fullscreen.vsh", ShaderStageKind.Vertex),
            fragment ?? Stage(structural: [Enabled], constants: [new(4, Steps, ShaderCondition.Equal(Enabled, true))])],
            budget, options ?? [Enabled, Steps], groups, assignments);

    /// <summary>Creates a canonical structural row for explicit supported subsets.</summary>
    public static IReadOnlyDictionary<string, string> Row(params (string Name, string Value)[] values) =>
        values.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
    #endregion
}
