using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies reusable pipeline identity over existing immutable shader selections.</summary>
public sealed class ShaderPipelineIdentityTests
{
    #region Public API
    /// <summary>Identity shares immutable stage selections instead of duplicating their binding payloads.</summary>
    [Fact]
    public void IdentityRetainsLoadPlanSelections()
    {
        var plan = Plan();
        var identity = new ShaderPipelineIdentity("test", plan);
        foreach (var stage in plan.Stages)
            Assert.Same(stage, identity.Stages.Single(value => value.Stage.Kind == stage.Stage.Kind));
        Assert.Throws<NotSupportedException>(() => ((IList<ShaderStageSelection>)identity.Stages).Clear());
    }

    /// <summary>Independent maps with different insertion order retain equal structural identity and hashes.</summary>
    [Fact]
    public void MapInsertionOrderDoesNotChangeIdentity()
    {
        var first = new ShaderPipelineIdentity("test", Plan());
        var second = new ShaderPipelineIdentity("test", Plan(reverse: true));
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.True(new HashSet<ShaderPipelineIdentity> { first }.Contains(second));
    }

    /// <summary>Effective shader configuration and asset ownership cannot alias a previous identity.</summary>
    [Fact]
    public void SelectedInputsAndInterfaceDeclarationsChangeIdentity()
    {
        var baseline = new ShaderPipelineIdentity("test", Plan());
        ShaderLoadPlan[] variants = [Plan(quality: 2), Plan(slot: 3), Plan(define: 2),
            Plan(location: 2), Plan(entry: "alternate"), Plan(binary: "other.vsh")];
        foreach (var plan in variants)
            Assert.NotEqual(baseline, new ShaderPipelineIdentity("test", plan));
        Assert.NotEqual(baseline, new ShaderPipelineIdentity("other", Plan()));
    }

    /// <summary>Unused option domain choices do not distinguish the same selected executable inputs.</summary>
    [Fact]
    public void DeclarationDomainsDoNotChangeEffectiveIdentity()
    {
        var firstPlan = Plan();
        var secondPlan = Plan(expandedDomain: true);
        Assert.False(firstPlan.Stages[0].SameInputs(secondPlan.Stages[0]));
        var first = new ShaderPipelineIdentity("test", firstPlan);
        var second = new ShaderPipelineIdentity("test", secondPlan);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    /// <summary>Mutating declaration inputs cannot alter shared immutable stage data or cached hashes.</summary>
    [Fact]
    public void CallerMutationCannotAlterPublishedIdentity()
    {
        var bindings = new GpuBindingContract();
        bindings.RegisterUniformBlockBinding("Parameters", 0);
        var defines = new Dictionary<string, ShaderScalar> { ["FIXED"] = ShaderScalar.From(1) };
        var stage = new ShaderStageContract("test.vsh", "test.vsh", ShaderStageKind.Vertex, bindings, fixedDefines: defines);
        var contract = new GpuShaderContract("test", [stage,
            new ShaderStageContract("test.fsh", "test.fsh", ShaderStageKind.Fragment, new())], 1);
        var identity = new ShaderPipelineIdentity("test", new(new ShaderSettings(contract)));
        int hash = identity.GetHashCode();
        bindings.UniformBlocks["Parameters"] = new(5, true);
        defines["FIXED"] = ShaderScalar.From(2);
        Assert.Equal(0, identity.Stages[0].Stage.Bindings.UniformBlocks["Parameters"].Slot);
        Assert.Equal(ShaderScalar.From(1), identity.Stages[0].Stage.FixedDefines["FIXED"]);
        Assert.Equal(hash, identity.GetHashCode());
        Assert.Throws<NotSupportedException>(() => identity.Stages[0].Stage.Bindings.UniformBlocks.Clear());
    }
    #endregion

    #region Private
    /// <summary>Constructs independently owned declarations with selectable effective input differences.</summary>
    private static ShaderLoadPlan Plan(bool reverse = false, int quality = 1, int slot = 0,
        int define = 1, int location = 0, string entry = "main", string binary = "test.vsh", bool expandedDomain = false)
    {
        var bindings = new GpuBindingContract();
        var defines = new Dictionary<string, ShaderScalar>();
        // Reverse both collections to exercise order-independent map hashing.
        foreach (string name in reverse ? new[] { "SECOND", "FIRST" } : new[] { "FIRST", "SECOND" })
        {
            bindings.RegisterUniformBlockBinding(name, name == "FIRST" ? slot : 4);
            defines.Add(name, ShaderScalar.From(define));
        }
        bindings.UniformLocations.Add("value", location);
        var option = new ShaderOption<int>("QUALITY", 1, expandedDomain ? [1, 2, 3] : [1, 2]);
        var stage = new ShaderStageContract("test.vsh", "test.vsh", ShaderStageKind.Vertex, bindings,
            specializations: [new(0, option)], fixedDefines: defines, entryPoint: entry, binaryAsset: binary);
        var contract = new GpuShaderContract("test", [stage,
            new ShaderStageContract("test.fsh", "test.fsh", ShaderStageKind.Fragment, new())], 1, options: [option]);
        return new(new ShaderSettings(contract).With(option, quality));
    }
    #endregion
}
