using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests.Unit;

public sealed class LumOnDebugViewsPhase5UnitTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void LumOnDebugMode_ProbeAtlasPhase3Modes_AreAppendOnlyAndStable()
    {
        // Guard against accidental renumbering (append-only contract).
        Assert.Equal(44, (int)LumOnDebugMode.WorldProbeSuppressedLighting);

        Assert.Equal(45, (int)LumOnDebugMode.ProbeAtlasCurrentRadiance);
        Assert.Equal(46, (int)LumOnDebugMode.ProbeAtlasGatherInputRadiance);
        Assert.Equal(47, (int)LumOnDebugMode.ProbeAtlasHitDistance);
        Assert.Equal(48, (int)LumOnDebugMode.ProbeAtlasTraceRadiance);
        Assert.Equal(68, (int)LumOnDebugMode.WorldProbeImportance);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DebugViewerToolsHooks_AreSafeWhenUninitialized()
    {
        // These are intentionally safe to call even when the mod system is not running.
        Assert.False(VgeDebugViewerManager.IsDialogOpen());
        Assert.False(VgeDebugViewerManager.ToggleDialog());
    }
}
