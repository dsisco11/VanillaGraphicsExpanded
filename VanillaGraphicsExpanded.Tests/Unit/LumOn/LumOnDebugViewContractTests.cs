using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn;

/// <summary>Checks stable debug-mode identifiers and safe debug-view calls before initialization.</summary>
public sealed class LumOnDebugViewContractTests
{
    #region Public API
    /// <summary>Probe-atlas debug values are appended without renumbering established values.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void DebugModeIdentifiersRetainAssignedValues()
    {
        // Arrange
        // Guard against accidental renumbering (append-only contract).
        // Act and Assert
        Assert.Equal(44, (int)LumOnDebugMode.WorldProbeSuppressedLighting);

        Assert.Equal(45, (int)LumOnDebugMode.ProbeAtlasCurrentRadiance);
        Assert.Equal(46, (int)LumOnDebugMode.ProbeAtlasGatherInputRadiance);
        Assert.Equal(47, (int)LumOnDebugMode.ProbeAtlasHitDistance);
        Assert.Equal(48, (int)LumOnDebugMode.ProbeAtlasTraceRadiance);
        Assert.Equal(68, (int)LumOnDebugMode.WorldProbeImportance);
    }

    /// <summary>Debug-view status and toggle queries are safe before the manager is initialized.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void DebugViewerToolsHooks_AreSafeWhenUninitialized()
    {
        // Arrange
        // These are intentionally safe to call even when the mod system is not running.
        // Act and Assert
        Assert.False(VgeDebugViewerManager.IsDialogOpen());
        Assert.False(VgeDebugViewerManager.ToggleDialog());
    }
    #endregion
}
