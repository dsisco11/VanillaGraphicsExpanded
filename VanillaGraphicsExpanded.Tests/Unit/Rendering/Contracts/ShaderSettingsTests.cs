using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts.Fixtures.ShaderContractFixture;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Program-level normalization, alias and immutable snapshot behavior.</summary>
public sealed class ShaderSettingsTests
{
    #region Public API
    #region Settings and aliases
    /// <summary>Equivalent aliases agree; conflicting values identify the program, option and value.</summary>
    [Fact]
    public void AliasesAreDeclaredAndConflictsFail()
    {
        // Arrange: Supply equivalent spellings for one option, then conflicting spellings.
        // Act: Normalize the first map and attempt to construct settings from the second.
        var program = Program();
        var equal = new ShaderSettings(program, new Dictionary<string, string?> { ["ENABLED"] = "1.0", ["LEGACY_ENABLED"] = "true" });
        // Assert: Aliases collapse to their canonical key and conflicting values report their context.
        Assert.Equal(ShaderScalar.From(true), equal.Values["ENABLED"]);
        Assert.False(equal.Values.ContainsKey("LEGACY_ENABLED"));
        var error = Assert.Throws<ArgumentException>(() => new ShaderSettings(program,
            new Dictionary<string, string?> { ["ENABLED"] = "0", ["LEGACY_ENABLED"] = "1" }));
        Assert.Contains("fixture", error.Message); Assert.Contains("ENABLED", error.Message); Assert.Contains("1", error.Message);
    }

    /// <summary>Changing an instance, using a legacy setter or resetting null does not mutate defaults or prior snapshots.</summary>
    [Fact]
    public void InstancesDefaultsAndNullResetAreIndependent()
    {
        // Arrange: Create independent settings snapshots from the same immutable program.
        var program = Program();
        var first = new ShaderSettings(program);
        var second = new ShaderSettings(program).With(Enabled, true).With(Steps, 22);
        // Act: Reset a legacy key and numeric option in the second snapshot.
        var reset = second.With("LEGACY_ENABLED", null).With("STEPS", null);
        // Assert: The reset snapshot matches defaults while earlier instances remain unchanged.
        Assert.Equal(ShaderScalar.From(false), first.Values["ENABLED"]);
        Assert.Equal(ShaderScalar.From(22), second.Values["STEPS"]);
        Assert.Equal(first.Values.OrderBy(p => p.Key), reset.Values.OrderBy(p => p.Key));
        Assert.Equal(ShaderScalar.From(true), second.Values["ENABLED"]);
        Assert.Equal(ShaderScalar.From(false), Enabled.Default);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, ShaderScalar>)second.Values).Clear());
    }

    /// <summary>Input maps are copied, including requested values that currently have no effective stage use.</summary>
    [Fact]
    public void InactiveSelectionsSurviveFeatureToggles()
    {
        // Arrange: Retain a caller-owned input map with an inactive numeric selection.
        var program = Program(); var resolver = new ShaderVariantResolver([program]);
        var input = new Dictionary<string, string?> { ["STEPS"] = "37" };
        var off = new ShaderSettings(program, input); input["STEPS"] = "5";
        // Act: Enable the option and resolve the off/on plans.
        var on = off.With(Enabled, true);
        // Assert: The inactive choice remains stored and only becomes effective when enabled.
        Assert.Empty(resolver.Resolve(off)[1].Specializations);
        Assert.Equal(ShaderScalar.From(37), Assert.Single(resolver.Resolve(on)[1].Specializations).Value);
        Assert.True(resolver.Resolve(off)[1].SameInputs(resolver.Resolve(off.With(Steps, 12))[1]));
        Assert.Equal(ShaderScalar.From(37), off.Values["STEPS"]);
    }

    /// <summary>Unknown options and mismatched typed keys fail before creating any effective selection.</summary>
    [Fact]
    public void UnknownAndIncompatibleSettingsFail()
    {
        // Arrange: Capture the default settings for a program with a typed integer option and alias.
        var settings = new ShaderSettings(Program());
        // Act: Submit each unknown, mismatched, and invalid value through the settings API.
        // Assert: The API rejects every unsupported update with its declared exception and context.
        Assert.Contains("UNKNOWN", Assert.Throws<ArgumentException>(() => settings.With("UNKNOWN", null)).Message);
        Assert.Throws<ArgumentException>(() => settings.With(new ShaderOption<float>("STEPS", 10f), 12f));
        var error = Assert.Throws<ArgumentException>(() => settings.With("STEPS", "2.5"));
        Assert.Contains("fixture", error.Message); Assert.Contains("STEPS", error.Message); Assert.Contains("2.5", error.Message);
        Assert.Throws<ArgumentException>(() => settings.With(Steps, 101));
    }

    /// <summary>Reusable groups explicitly declare program membership while remaining outside stage identity.</summary>
    [Fact]
    public void GroupsAreExplicitAndSnapshotTheirInputs()
    {
        // Arrange: Create a mutable option list and snapshot it into a group.
        ShaderOption[] options = [Enabled, Steps];
        var group = new ShaderOptionGroup("trace", options);
        options[0] = new ShaderOption<bool>("OTHER", false);
        // Act: Build a program from the captured group and request membership by both names.
        var program = Program(options: [], groups: [group]);
        // Assert: Only the original immutable members participate in the program.
        Assert.Same(Enabled, program.FindOption("LEGACY_ENABLED"));
        Assert.Throws<ArgumentException>(() => program.FindOption("OTHER"));
        Assert.Throws<ArgumentException>(() => Program(options: [], groups: [group, group]));
    }
    #endregion
    #endregion
}
