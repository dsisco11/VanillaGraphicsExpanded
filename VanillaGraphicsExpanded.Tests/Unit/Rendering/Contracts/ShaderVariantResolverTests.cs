using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts.ShaderContractFixture;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Pure stage projection, conditional inputs, sharing and deterministic publication checks.</summary>
public sealed class ShaderVariantResolverTests
{
    #region Projection and conditions
    /// <summary>Fragment options leave the shared vertex binary unchanged; numeric options do not enter binary keys.</summary>
    [Fact]
    public void FragmentAndNumericChangesHaveSeparateIdentities()
    {
        var program = Program(); var resolver = new ShaderVariantResolver([program]);
        var original = new ShaderSettings(program);
        var off = resolver.Resolve(original);
        var on = resolver.Resolve(original.With("LEGACY_ENABLED", "1.0"));
        var tuned = resolver.Resolve(original.With(Enabled, true).With(Steps, 21));
        Assert.Equal("fixture.fsh.spv", off[1].BinaryPath);
        Assert.Equal("ENABLED=1", on[1].Key);
        Assert.StartsWith("variants/fixture.fsh/", on[1].BinaryPath);
        Assert.Equal(on[1].BinaryPath, tuned[1].BinaryPath);
        Assert.False(on[1].SameInputs(tuned[1]));
        Assert.True(off[0].SameInputs(on[0]));
        Assert.Equal(3, resolver.Binaries.Count);
    }

    /// <summary>One finite typed value can drive a structural stage and a specialization in another stage.</summary>
    [Fact]
    public void OneSnapshotFeedsDifferentStageUses()
    {
        var mode = new ShaderOption<int>("MODE", 0, [0, 2]);
        var vertex = Stage("v", ShaderStageKind.Vertex, constants: [new(7, mode)]);
        var fragment = Stage(structural: [mode]);
        var program = Program(fragment: fragment, options: [mode], vertex: vertex);
        var resolver = new ShaderVariantResolver([program]);
        var result = resolver.Resolve(new ShaderSettings(program).With(mode, 2));
        Assert.Equal(2u, Assert.Single(result[0].Specializations).Value.Bits);
        Assert.Equal(7u, Assert.Single(result[0].Specializations).Id);
        Assert.Equal("MODE=2", result[1].Key);
        Assert.Empty(result[0].Structural);
        Assert.Equal(3, resolver.Binaries.Count);
        Assert.Equal(ShaderScalar.From(0), Assert.Single(resolver.Binaries.Single(b => b.Stage.Kind == ShaderStageKind.Vertex).Specializations).Value);
    }

    /// <summary>All/Any/Not conditions are shared structural declarations, independent of program support.</summary>
    [Fact]
    public void ConditionsControlOnlySpecializationAvailability()
    {
        var batch = new ShaderOption<bool>("BATCH", false);
        var uniform = new ShaderOption<bool>("UNIFORM", false);
        var condition = ShaderCondition.All(ShaderCondition.Equal(Enabled, true),
            ShaderCondition.Not(ShaderCondition.Any(ShaderCondition.Equal(batch, true), ShaderCondition.Equal(uniform, true))));
        var program = Program(fragment: Stage(structural: [Enabled, batch, uniform], constants: [new(5, Steps, condition)]),
            options: [Enabled, batch, uniform, Steps], budget: 8);
        var resolver = new ShaderVariantResolver([program]);
        Assert.Equal(8, program.Assignments.Count);
        int active = 0;
        foreach (var row in program.Assignments)
        {
            var settings = new ShaderSettings(program, row.ToDictionary(p => p.Key, p => (string?)p.Value.Canonical));
            active += resolver.Resolve(settings)[1].Specializations.Count;
        }
        Assert.Equal(1, active);
        Assert.Throws<ArgumentException>(() => Stage(constants: [new(0, Steps, ShaderCondition.Equal(Enabled, true))]));
        Assert.Throws<ArgumentException>(() => Stage(constants: [new(0, Steps, ShaderCondition.Equal(Steps, 10))]));
        Assert.Throws<ArgumentException>(() => ShaderCondition.Equal(Enabled, true).Validate([], "missing"));
    }

    /// <summary>Keys sort canonical names and are stable across declaration/value order and culture-independent spellings.</summary>
    [Fact]
    public void KeysAndPathsAreDeterministic()
    {
        var a = new ShaderOption<int>("A", 0, [0, 2]); var z = new ShaderOption<bool>("Z", true);
        var first = Program(fragment: Stage(structural: [z, a]), options: [z, a], budget: 4);
        var second = Program(fragment: Stage(structural: [a, z]), options: [a, z], budget: 4);
        var one = new ShaderVariantResolver([first]).Resolve(new ShaderSettings(first, new Dictionary<string, string?> { ["Z"] = "0", ["A"] = "2.0" }))[1];
        var two = new ShaderVariantResolver([second]).Resolve(new ShaderSettings(second, new Dictionary<string, string?> { ["A"] = "2", ["Z"] = "false" }))[1];
        Assert.Equal("A=2;Z=0", one.Key); Assert.Equal(one.BinaryPath, two.BinaryPath);
        Assert.Equal("variants/fixture.fsh/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("A=2;Z=0"))).ToLowerInvariant() + ".spv", one.BinaryPath);
    }
    #endregion

    #region Registry integrity
    /// <summary>Shared stage definitions deduplicate regardless of which program is enumerated first.</summary>
    [Fact]
    public void SharedDefinitionsDeduplicateWithoutConsumerOrderDependence()
    {
        var first = Program("first"); var second = Program("second");
        var a = new ShaderVariantResolver([first, second]); var b = new ShaderVariantResolver([second, first]);
        Assert.Equal(3, a.Binaries.Count);
        Assert.Equal(a.Binaries.Select(s => s.BinaryPath), b.Binaries.Select(s => s.BinaryPath));
        Assert.Throws<ArgumentException>(() => a.FindProgram("missing"));
        Assert.Throws<ArgumentException>(() => a.FindStage("missing.fsh"));
        Assert.Throws<ArgumentException>(() => a.Resolve(new ShaderSettings(Program("first"))));
        Assert.Throws<ArgumentException>(() => new ShaderVariantResolver([first, first]));
    }

    /// <summary>Conflicting source, fixed values, bindings, uses and entry points require distinct stage identities.</summary>
    [Fact]
    public void ConflictingSharedStagesFail()
    {
        var original = Program();
        var binding = new GpuBindingContract(); binding.RegisterUniformBlockBinding("Block", 2);
        ShaderStageContract[] conflicts =
        [
            Stage(source: "other.fsh", structural: [Enabled]),
            Stage(structural: [Enabled], bindings: binding),
            Stage(structural: [Enabled], fixedDefines: new Dictionary<string, ShaderScalar> { ["FIXED"] = ShaderScalar.From(true) }),
            Stage(structural: [Enabled], constants: [new(8, Steps)]),
            new("fixture.fsh", "fixture.fsh", ShaderStageKind.Fragment, new(), [Enabled], entryPoint: "another")
        ];
        foreach (var stage in conflicts)
        {
            var error = Assert.Throws<ArgumentException>(() => new ShaderVariantResolver([original, Program("other", stage)]));
            Assert.Contains("fixture.fsh", error.Message);
        }
    }

    /// <summary>Different stage identities cannot publish to one portable output path, including case aliases.</summary>
    [Fact]
    public void OutputPathsAreUniqueAcrossIdentities()
    {
        var first = Program("first", Stage("a", binaryAsset: "shared", structural: [Enabled]));
        var second = Program("second", Stage("b", binaryAsset: "SHARED", structural: [Enabled]));
        Assert.Throws<ArgumentException>(() => new ShaderVariantResolver([first, second]));
        var distinct = Program("second", Stage("b", source: "a", binaryAsset: "separate", structural: [Enabled]));
        Assert.Equal(5, new ShaderVariantResolver([first, distinct]).Binaries.Count);
    }

    /// <summary>Numeric bool/uint specializations preserve declared type and stable explicit IDs in the load plan.</summary>
    [Fact]
    public void NumericTypesUseExplicitIds()
    {
        var enabled = new ShaderOption<bool>("SWITCH", false);
        var unsigned = new ShaderOption<uint>("COUNT", uint.MaxValue);
        var program = new GpuShaderContract("compute", [Stage("c", ShaderStageKind.Compute,
            constants: [new(12, enabled), new(3, unsigned)])], 1, [enabled, unsigned]);
        var result = Assert.Single(new ShaderVariantResolver([program]).Resolve(new ShaderSettings(program).With(enabled, true)));
        Assert.Equal(new uint[] { 3, 12 }, result.Specializations.Select(s => s.Id));
        Assert.Equal(ShaderScalarType.UInt, result.Specializations[0].Value.Type);
        Assert.Equal(uint.MaxValue, result.Specializations[0].Value.Bits);
        Assert.Equal(ShaderScalarType.Bool, result.Specializations[1].Value.Type);
        Assert.Equal(1u, result.Specializations[1].Value.Bits);
    }
    #endregion
}
