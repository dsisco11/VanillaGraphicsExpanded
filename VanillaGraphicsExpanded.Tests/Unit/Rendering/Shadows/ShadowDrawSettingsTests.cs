using Newtonsoft.Json;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Protects persistence and finite recovery for scene preparation and draw budgets.</summary>
public sealed class ShadowDrawSettingsTests
{
    #region Public API
    /// <summary>Drawing limits survive the owning configuration's JSON round trip and freeze independently.</summary>
    [Fact]
    public void DrawingLimitsPersistAndFreeze()
    {
        // Arrange: Set non-default limits through the owning user configuration.
        var config = new VgeConfig();
        config.VirtualShadows.Drawing = new ShadowDrawSettings
        {
            MemoryMiB = 48, GroupVisits = 120, RangeVisits = 240, EntityVisits = 60,
            PageCapacity = 96, PairCapacity = 512, ProbeLimit = 32,
            ExpansionVisits = 2048, CommandSlots = 1024, PageInstances = 768,
            EstimatedTriangles = 1_250_000
        };

        // Act: Restore and sanitize through the configuration owner's normal contract.
        var restored = JsonConvert.DeserializeObject<VgeConfig>(JsonConvert.SerializeObject(config))!;
        restored.Sanitize();
        var effective = restored.VirtualShadows.Drawing.Freeze();

        // Assert: The configured work and memory limits are retained in a detached effective snapshot.
        Assert.Equal(48, effective.MemoryMiB);
        Assert.Equal(120, effective.GroupVisits);
        Assert.Equal(240, effective.RangeVisits);
        Assert.Equal(60, effective.EntityVisits);
        Assert.Equal(96, effective.PageCapacity);
        Assert.Equal(512, effective.PairCapacity);
        Assert.Equal(32, effective.ProbeLimit);
        Assert.Equal(2048, effective.ExpansionVisits);
        Assert.Equal(1024, effective.CommandSlots);
        Assert.Equal(768, effective.PageInstances);
        Assert.Equal(1_250_000, effective.EstimatedTriangles);
        restored.VirtualShadows.Drawing.CommandSlots = 8;
        Assert.Equal(1024, effective.CommandSlots);
    }

    /// <summary>Invalid, excessive and malformed limits recover to documented finite defaults.</summary>
    [Fact]
    public void InvalidDrawingLimitsRecoverWithoutTruncatingOtherValues()
    {
        // Arrange: Mix invalid values, an unsupported hash size and valid independent overrides.
        var settings = new ShadowDrawSettings
        {
            MemoryMiB = 0, GroupVisits = -1, RangeVisits = 2_000_000, EntityVisits = 64,
            PageCapacity = 9000, PairCapacity = 768, ProbeLimit = 0,
            ExpansionVisits = 20_000_000, CommandSlots = 24, PageInstances = -2,
            EstimatedTriangles = int.MaxValue
        };

        // Act: Apply the same field-level sanitizer used when live configuration is loaded.
        settings.Sanitize();
        var frozen = settings.Freeze();

        // Assert: Invalid fields recover independently while valid controls remain configurable.
        Assert.Equal(32, frozen.MemoryMiB);
        Assert.Equal(4096, frozen.GroupVisits);
        Assert.Equal(65536, frozen.RangeVisits);
        Assert.Equal(64, frozen.EntityVisits);
        Assert.Equal(128, frozen.PageCapacity);
        Assert.Equal(32768, frozen.PairCapacity);
        Assert.Equal(128, frozen.ProbeLimit);
        Assert.Equal(1_048_576, frozen.ExpansionVisits);
        Assert.Equal(24, frozen.CommandSlots);
        Assert.Equal(32768, frozen.PageInstances);
        Assert.Equal(int.MaxValue, frozen.EstimatedTriangles);
    }
    #endregion
}
