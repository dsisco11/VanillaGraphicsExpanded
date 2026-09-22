using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts.ShaderContractFixture;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Verifies immutable runtime plans independently from GPU allocation and catalog membership.</summary>
public sealed class ShaderLoadPlanTests
{
    #region Runtime snapshots
    /// <summary>Inactive numeric selections survive but affect effective inputs only when their condition becomes active.</summary>
    [Fact]
    public void InactiveSelectionsAliasesAndResetsProduceCoherentPlans()
    {
        var original = new ShaderLoadPlan(new ShaderSettings(Program()));
        var inactive = new ShaderLoadPlan(original.Settings.With(Steps, 23));
        Assert.True(original.SameInputs(inactive));
        Assert.Equal("23", inactive.Settings.Values["STEPS"].Canonical);
        var active = new ShaderLoadPlan(inactive.Settings.With("LEGACY_ENABLED", "1"));
        Assert.False(original.SameInputs(active));
        Assert.True(original.Stages[0].SameInputs(active.Stages[0]));
        Assert.Equal(23u, Assert.Single(active.Stages[1].Specializations).Value.Bits);
        Assert.Equal("10", original.Settings.Values["STEPS"].Canonical);
        Assert.True(active.SameInputs(new ShaderLoadPlan(active.Settings.With("ENABLED", "true"))));
        var reset = new ShaderLoadPlan(active.Settings.With("LEGACY_ENABLED", null));
        Assert.True(original.SameInputs(reset));
        Assert.Equal("23", reset.Settings.Values["STEPS"].Canonical);
        Assert.Throws<ArgumentException>(() => active.Settings.With("UNKNOWN", null));
        Assert.Throws<ArgumentException>(() => active.Settings.With(Steps, 101));
    }
    #endregion
}
