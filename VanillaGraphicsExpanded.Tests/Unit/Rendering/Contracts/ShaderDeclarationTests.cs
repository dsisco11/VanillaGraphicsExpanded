using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts.ShaderContractFixture;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Fail-fast validation of immutable program and stage declarations.</summary>
public sealed class ShaderDeclarationTests
{
    #region Domains and declaration validation
    /// <summary>Defaults, finite domains and ranges are checked at option construction.</summary>
    [Fact]
    public void InvalidDomainsDefaultsAndTypesFail()
    {
        Assert.Throws<ArgumentException>(() => new ShaderOption<int>("MODE", 1, [0, 2]));
        Assert.Throws<ArgumentException>(() => new ShaderOption<int>("MODE", 0, []));
        Assert.Throws<ArgumentException>(() => new ShaderOption<int>("MODE", 0, [0, 0]));
        Assert.Throws<ArgumentException>(() => new ShaderOption<int>("MODE", 0, minimum: 2, maximum: 1));
        Assert.Throws<ArgumentException>(() => new ShaderOption<float>("VALUE", float.NaN));
        Assert.Throws<ArgumentException>(() => new ShaderOption<int>("MODE", 0, [0, 2], maximum: 1));
        Assert.Throws<ArgumentException>(() => Stage(structural: [new ShaderOption<int>("MODE", 0)]));
        Assert.Throws<ArgumentException>(() => Stage(structural: [new ShaderOption<float>("MODE", 0, [0, 1])]));
        Assert.Throws<ArgumentException>(() => new ShaderOption<bool>("FLAG", false, aliases: ["FLAG"]));
    }

    /// <summary>Fixed values and stage-owned IDs cannot collide with configurable names.</summary>
    [Fact]
    public void FixedNamesIdsAndAliasesAreValidated()
    {
        Assert.Throws<ArgumentException>(() => Stage(structural: [Enabled], fixedDefines: new Dictionary<string, ShaderScalar> { ["LEGACY_ENABLED"] = ShaderScalar.From(1) }));
        Assert.Throws<ArgumentException>(() => Stage(constants: [new(2, Steps), new(2, new ShaderOption<float>("OTHER", 1))]));
        Assert.Throws<ArgumentException>(() => new ShaderSpecialization(-1, Steps));
        Assert.Throws<ArgumentException>(() => Stage(structural: [Enabled], constants: [new(0, Enabled)]));
        foreach (string name in new[] { "gl_reserved", "GL_RESERVED", "A__B", "defined", "1BAD", "bad-name" })
            Assert.Throws<ArgumentException>(() => new ShaderOption<bool>(name, false));
        Assert.Throws<ArgumentException>(() => Program(fragment: Stage(fixedDefines: new Dictionary<string, ShaderScalar> { ["ENABLED"] = ShaderScalar.From(0) })));
        Assert.Throws<ArgumentException>(() => Program(options: [Enabled, Steps, new ShaderOption<bool>("LEGACY_ENABLED", false)]));
    }

    /// <summary>Programs explicitly declare valid stage combinations and every accepted stage setting.</summary>
    [Fact]
    public void InvalidStageCombinationsAndUndeclaredUsesFail()
    {
        Assert.Throws<ArgumentException>(() => new GpuShaderContract("bad", [], 1));
        Assert.Throws<ArgumentException>(() => new GpuShaderContract("bad", [Stage()], 1));
        Assert.Throws<ArgumentException>(() => new GpuShaderContract("bad", [Stage("compute", ShaderStageKind.Compute), Stage()], 1));
        Assert.Throws<ArgumentException>(() => new GpuShaderContract("bad", [Stage("v", ShaderStageKind.Vertex), Stage(), Stage("tc", ShaderStageKind.TessellationControl)], 1));
        Assert.Throws<ArgumentException>(() => Program(options: [Enabled]));
        Assert.Throws<ArgumentException>(() => Stage(kind: (ShaderStageKind)123));
        Assert.Throws<ArgumentException>(() => Stage(source: "../escape.fsh"));
        Assert.Throws<ArgumentException>(() => Stage(binaryAsset: "C:/escape"));
        Assert.Throws<ArgumentException>(() => new ShaderStageContract("x", "x", ShaderStageKind.Compute, new(), entryPoint: "not valid"));
        Assert.Single(new GpuShaderContract("compute", [Stage("c", ShaderStageKind.Compute)], 1).Assignments);
        Assert.Equal(5, new GpuShaderContract("graphics", [Stage("v", ShaderStageKind.Vertex), Stage(),
            Stage("g", ShaderStageKind.Geometry), Stage("tc", ShaderStageKind.TessellationControl), Stage("te", ShaderStageKind.TessellationEvaluation)], 1).Stages.Count);
        // Evaluation may use default patch levels without an active control stage.
        Assert.Equal(3, new GpuShaderContract("default-patch-levels", [Stage("v", ShaderStageKind.Vertex), Stage(),
            Stage("te", ShaderStageKind.TessellationEvaluation)], 1).Stages.Count);
    }

    /// <summary>Collection arguments and binding dictionaries cannot mutate published stage declarations.</summary>
    [Fact]
    public void ContractsFreezeAllDeclarationInputs()
    {
        var bindings = new GpuBindingContract(); bindings.RegisterSamplerUnit("source", 3);
        var structure = new List<ShaderOption> { Enabled };
        var constants = new List<ShaderSpecialization> { new(0, Steps) };
        var fixedValues = new Dictionary<string, ShaderScalar> { ["FIXED"] = ShaderScalar.From(1) };
        var stage = Stage(structural: structure, constants: constants, bindings: bindings, fixedDefines: fixedValues);
        structure.Clear(); constants.Clear(); fixedValues.Clear(); bindings.Samplers.Clear();
        Assert.Single(stage.Structural); Assert.Single(stage.Specializations); Assert.Single(stage.FixedDefines);
        Assert.Equal(3, stage.Bindings.Samplers["source"].Slot);
        Assert.Throws<NotSupportedException>(() => stage.Bindings.Samplers.Clear());
        Assert.Throws<NotSupportedException>(() => stage.Bindings.RegisterSamplerUnit("other", 4));
        Assert.Throws<NotSupportedException>(() => ((IList<ShaderOption>)stage.Structural).Clear());
    }
    #endregion

    #region Supported assignments
    /// <summary>Complete explicit subsets restrict program membership without inventing availability rules.</summary>
    [Fact]
    public void ExplicitSubsetRequiresDefaultAndCompleteUniqueRows()
    {
        var mode = new ShaderOption<int>("MODE", 0, [0, 1, 2]);
        var stage = Stage(structural: [Enabled, mode]);
        var valid = Program(fragment: stage, options: [Enabled, mode], assignments:
            [Row(("ENABLED", "0"), ("MODE", "0")), Row(("ENABLED", "1"), ("MODE", "2"))]);
        Assert.Equal(2, valid.Assignments.Count);
        Assert.Throws<ArgumentException>(() => new ShaderSettings(valid).With(Enabled, true));
        Assert.Equal(ShaderScalar.From(2), new ShaderSettings(valid, new Dictionary<string, string?> { ["ENABLED"] = "1", ["MODE"] = "2" }).Values["MODE"]);
        foreach (var rows in new IReadOnlyDictionary<string, string>[][]
        {
            [], [Row(("ENABLED", "1"), ("MODE", "2"))], [Row(("ENABLED", "0"))],
            [Row(("ENABLED", "0"), ("MODE", "0")), Row(("ENABLED", "0.0"), ("MODE", "0"))],
            [Row(("ENABLED", "0"), ("MODE", "3"))], [Row(("ENABLED", "0"), ("UNKNOWN", "0"))],
            [Row(("LEGACY_ENABLED", "0"), ("MODE", "0"))]
        }) Assert.Throws<ArgumentException>(() => Program(fragment: stage, options: [Enabled, mode], assignments: rows));
    }

    /// <summary>Cartesian expansion is bounded before allocating the product; numeric options do not multiply it.</summary>
    [Fact]
    public void BudgetsBoundCartesianAndExplicitConfigurations()
    {
        Assert.Equal(2, Program().Assignments.Count);
        Assert.Throws<ArgumentException>(() => Program(budget: 1));
        Assert.Throws<ArgumentException>(() => Program(budget: 0));
        Assert.Throws<ArgumentException>(() => Program(budget: 1, assignments: [Row(("ENABLED", "0")), Row(("ENABLED", "1"))]));
        var options = Enumerable.Range(0, 40).Select(i => new ShaderOption<bool>("FLAG" + i, false)).ToArray();
        Assert.Throws<ArgumentException>(() => Program(fragment: Stage(structural: options), options: options, budget: int.MaxValue));
    }
    #endregion
}
