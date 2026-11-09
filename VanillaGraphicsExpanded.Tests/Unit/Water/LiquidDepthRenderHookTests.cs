using System.Reflection.Emit;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Liquids;

namespace VanillaGraphicsExpanded.Tests.Unit.Water;

/// <summary>Checks the liquid depth transpiler's malformed-input guard in isolation.</summary>
[Collection("CPU state")]
public sealed class LiquidDepthRenderHookTests
{
    #region Public API
    /// <summary>Empty synthetic IL is rejected before an unrelated engine draw range can be selected.</summary>
    [Fact]
    public void MissingDepthBoundaryIsRejected()
    {
        // Arrange a synthetic method body with no depth-render call boundary.
        var generator = new DynamicMethod("missingDepth", typeof(void), Type.EmptyTypes).GetILGenerator();

        // Act and assert the production transpiler fails explicitly on that unsupported input.
        Assert.Throws<InvalidOperationException>(() => LiquidDepthRenderHook.Transpiler([], generator).ToArray());
    }
    #endregion
}
