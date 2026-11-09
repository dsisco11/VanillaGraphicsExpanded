using VanillaGraphicsExpanded.PBR.Postprocessing;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Verifies native graphics settings remain authoritative at the owned postprocessing boundary.</summary>
[Collection("CPU state")]
public sealed class EnginePostprocessInputsTests
{
    #region Public API
    /// <summary>A transient pass gate masks effects without changing cached qualities or persistent eligibility.</summary>
    [Fact]
    public void PassGatePreservesCachedSettingsAndQuality()
    {
        // Arrange: retain cached eligibility and quality while a single pass is disabled.
        var cached = new EnginePostprocessInputs(true, true, true, true, 2, 3);
        // Act: derive pass-local settings without mutating the cached inputs.
        var disabled = cached.ForPass(false);
        // Assert: the pass is masked while cached qualities and later enabled behavior remain intact.
        Assert.False(disabled.Bloom); Assert.False(disabled.LightShafts);
        Assert.False(disabled.Ssao); Assert.False(disabled.Fxaa);
        Assert.Equal(2, disabled.SsaoQuality); Assert.Equal(3, disabled.LightShaftQuality);
        Assert.True(cached.Ssao); Assert.True(cached.LightShafts);
        Assert.Equal(cached, cached.ForPass(true));
    }

    /// <summary>Persisted bloom controls retain bypass semantics and bound finite graph work.</summary>
    [Fact]
    public void BloomSnapshotPreservesBypassAndBoundsGraph()
    {
        // Arrange: include explicit bypass values and an excessive level count.
        var settings=new PostprocessSettings {BloomThreshold=0,BloomKnee=0,BloomLevels=99};
        // Act: snapshot the authored values.
        var snapshot=settings.Snapshot();
        // Assert: zero preserves bypass semantics and work is bounded.
        Assert.Equal(0,snapshot.BloomThreshold);Assert.Equal(0,snapshot.BloomKnee);Assert.Equal(6,snapshot.BloomLevels);
        // Act: snapshot invalid values after replacing the authored settings.
        settings.BloomThreshold=float.NaN;settings.BloomStrength=float.PositiveInfinity;settings.BloomLevels=-1;
        snapshot=settings.Snapshot();
        // Assert: invalid values return to the documented defaults.
        Assert.Equal(1,snapshot.BloomThreshold);Assert.Equal(.08f,snapshot.BloomStrength);Assert.Equal(3,snapshot.BloomLevels);
    }
    #endregion
}
