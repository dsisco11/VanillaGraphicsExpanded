using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Verifies captured asset selections independently of fixed RHI executable identity.</summary>
public sealed class ShaderLoadPlanSelectionTests
{
    #region Public API
    /// <summary>Cloning authored settings preserves preparation admission; independently authored contracts remain distinct.</summary>
    [Fact]
    public void SettingsSelectionsControlPreparationAdmission()
    {
        // Arrange
        var first = Plan();
        var changedQuality = Plan(quality: 2);
        var changedSlot = Plan(slot: 3);

        // Act
        bool clonedSelectionMatches = first.SameInputs(new(first.Settings));
        bool qualityChangeMatches = first.SameInputs(changedQuality);
        bool slotChangeMatches = first.SameInputs(changedSlot);

        // Assert
        Assert.True(clonedSelectionMatches);
        Assert.False(qualityChangeMatches);
        Assert.False(slotChangeMatches);
    }
    /// <summary>Caller mutation cannot alter selected bindings, fixed defines or the frozen stage collection.</summary>
    [Fact]
    public void CallerMutationCannotAlterCapturedSelections()
    {
        // Arrange
        var bindings = new GpuBindingContract();
        bindings.RegisterUniformBlockBinding("Parameters", 0);
        var defines = new Dictionary<string, ShaderScalar> { ["FIXED"] = ShaderScalar.From(1) };
        var stage = new ShaderStageContract("test.vsh", "test.vsh", ShaderStageKind.Vertex, bindings, fixedDefines: defines);
        var contract = new GpuShaderContract("test", [stage, new("test.fsh", "test.fsh", ShaderStageKind.Fragment, new())], 1);
        var plan = new ShaderLoadPlan(new ShaderSettings(contract));

        // Act
        bindings.UniformBlocks["Parameters"] = new(5, true);
        defines["FIXED"] = ShaderScalar.From(2);

        // Assert
        Assert.Equal(0,plan.Stages[0].Stage.Bindings.UniformBlocks["Parameters"].Slot);
        Assert.Equal(ShaderScalar.From(1),plan.Stages[0].Stage.FixedDefines["FIXED"]);
        Assert.Throws<NotSupportedException>(()=>((IList<ShaderStageSelection>)plan.Stages).Clear());
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
