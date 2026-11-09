using VanillaGraphicsExpanded.Rendering.Profiling;
using VanillaGraphicsExpanded.HarmonyPatches;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies malformed dispatcher input is rejected by the render-scope rewrite rule.</summary>
public sealed class EngineRenderScopeRewriteRulesTests
{
    #region Public API
    /// <summary>A changed engine dispatcher cannot silently leave renderer work unscoped.</summary>
    [Fact]
    public void MissingCallbacksAreRejected()
    {
        // Arrange an empty dispatcher instruction stream with no callback sites.
        // Act by applying the render-scope rewrite; assert the malformed stream is rejected.
        Assert.Throws<InvalidOperationException>(() => EngineRenderStageScopeHook.Transpiler([]));
    }
    #endregion
}
