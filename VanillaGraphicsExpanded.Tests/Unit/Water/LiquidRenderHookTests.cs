using System.Reflection.Emit;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Liquids;

namespace VanillaGraphicsExpanded.Tests.Unit.Water;

/// <summary>Checks the liquid render transpiler's malformed-input guard in isolation.</summary>
public sealed class LiquidRenderHookTests
{
    #region Public API
    /// <summary>Empty synthetic IL is rejected before unrelated renderer work can be suppressed.</summary>
    [Fact]
    public void MissingLiquidBoundaryIsRejected()
    {
        // Arrange a synthetic method body without the liquid suppression call.
        var generator = new DynamicMethod("missingLiquid", typeof(void), Type.EmptyTypes).GetILGenerator();

        // Act and assert unsupported input produces the explicit failure contract.
        Assert.Throws<InvalidOperationException>(() => LiquidRenderHooks.Transpiler([], generator).ToArray());
    }
    #endregion
}
