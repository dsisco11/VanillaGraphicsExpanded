using Newtonsoft.Json;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Protects persisted candidate budgets, field-level recovery and immutable policy snapshots.</summary>
public sealed class ShadowCandidateSettingsTests
{
    #region Public API
    /// <summary>User-configured limits round-trip through the owning mod config without becoming hard-coded policy.</summary>
    [Fact]
    public void CandidateLimitsPersistAndSanitizeIndependently()
    {
        // Arrange: persist non-default candidate storage and work allowances beside one malformed field.
        var config = new VgeConfig();
        config.VirtualShadows.Candidates = new ShadowCandidateSettings
        {
            MemoryMiB = 64, HeaderCapacity = 12, SegmentsPerAssociation = 3,
            DedupCapacity = 256, ProbeLimit = 32, ChangeCapacity = 300,
            SlabsPerSlice = 7, CellsPerSlice = 99, ProbesPerSlice = 400,
            ChangesPerSlice = 5, SlabsPerBatch = 70, CellsPerBatch = 900,
            ProbesPerBatch = 3000, ChangesPerBatch = 100, RefreshGuardFraction = 0.4
        };
        string json = JsonConvert.SerializeObject(config);

        // Act: restore and sanitize through the same VgeConfig owner used by runtime loading.
        var restored = JsonConvert.DeserializeObject<VgeConfig>(json)!;
        restored.Sanitize();
        var effective = restored.VirtualShadows.Candidates.Freeze();

        // Assert: configured limits survive persistence and the frozen policy is independent of later edits.
        Assert.Equal(64, effective.MemoryMiB);
        Assert.Equal(12, effective.HeaderCapacity);
        Assert.Equal(3, effective.SegmentsPerAssociation);
        Assert.Equal(256, effective.DedupCapacity);
        Assert.Equal(32, effective.ProbeLimit);
        Assert.Equal(300, effective.ChangeCapacity);
        Assert.Equal(7, effective.SlabsPerSlice);
        Assert.Equal(99, effective.CellsPerSlice);
        Assert.Equal(70, effective.SlabsPerBatch);
        Assert.Equal(0.4, effective.RefreshGuardFraction);
        restored.VirtualShadows.Candidates.CellsPerBatch = 17;
        Assert.Equal(900, effective.CellsPerBatch);
    }

    /// <summary>Malformed capacities recover to finite supported values without changing valid sibling overrides.</summary>
    [Fact]
    public void InvalidCandidateCapsRecoverToFiniteDefaults()
    {
        // Arrange: mix invalid signs, excessive ranges and non-power-of-two hash storage.
        var settings = new ShadowCandidateSettings
        {
            MemoryMiB = 0, HeaderCapacity = -2, SegmentsPerAssociation = 0,
            DedupCapacity = 100, ProbeLimit = 70000, ChangeCapacity = 2000,
            SlabsPerSlice = -1, CellsPerSlice = 50000000,
            SlabsPerBatch = 20, CellsPerBatch = 10000,
            RefreshGuardFraction = double.NaN
        };

        // Act: use the owning config sanitizer and freeze an immutable effective copy.
        settings.Sanitize();
        var frozen = settings.Freeze();

        // Assert: every invalid finite bound returns to its documented fallback while valid caps remain configurable.
        Assert.Equal(16, frozen.MemoryMiB);
        Assert.Equal(32, frozen.HeaderCapacity);
        Assert.Equal(64, frozen.SegmentsPerAssociation);
        Assert.Equal(8192, frozen.DedupCapacity);
        Assert.Equal(128, frozen.ProbeLimit);
        Assert.Equal(2000, frozen.ChangeCapacity);
        Assert.Equal(64, frozen.SlabsPerSlice);
        Assert.Equal(4096, frozen.CellsPerSlice);
        Assert.Equal(20, frozen.SlabsPerBatch);
        Assert.Equal(10000, frozen.CellsPerBatch);
        Assert.Equal(0.25, frozen.RefreshGuardFraction);
    }
    #endregion
}
