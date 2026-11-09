using VanillaGraphicsExpanded.LumOn;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn;

/// <summary>Checks world probe clipmap config behavior and contract rules.</summary>
public sealed class WorldProbeClipmapConfigTests
{
    #region Public API
    /// <summary>Verifies the sanitize clamps world probe clipmap config and resizes budgets behavior.</summary>
    [Fact]
    public void Sanitize_ClampsWorldProbeClipmapConfig_AndResizesBudgets()
    {
        // Arrange
        var cfg = new VgeConfig
        {
            WorldProbeClipmap = new VgeConfig.WorldProbeClipmapConfig
            {
                ClipmapBaseSpacing = -123f,
                ClipmapResolution = 1,
                ClipmapLevels = 7,
                OctahedralTileSize = 1,
                AtlasTexelsPerUpdate = -5,
                PerLevelProbeUpdateBudget = [1000],
                TraceMaxProbesPerFrame = -5,
                UploadBudgetBytesPerFrame = -1
            }
        };
        // Act
        cfg.Sanitize();
        // Assert
        Assert.InRange(cfg.WorldProbeClipmap.ClipmapBaseSpacing, 0.25f, 64f);
        Assert.InRange(cfg.WorldProbeClipmap.ClipmapResolution, 8, 128);
        Assert.InRange(cfg.WorldProbeClipmap.ClipmapLevels, 1, 8);
        Assert.InRange(cfg.WorldProbeClipmap.OctahedralTileSize, 8, 64);
        Assert.InRange(cfg.WorldProbeClipmap.AtlasTexelsPerUpdate, 1, cfg.WorldProbeClipmap.OctahedralTileSize * cfg.WorldProbeClipmap.OctahedralTileSize);
        Assert.Equal(cfg.WorldProbeClipmap.ClipmapLevels, cfg.WorldProbeClipmap.PerLevelProbeUpdateBudget.Length);
        Assert.Equal(0, cfg.WorldProbeClipmap.TraceMaxProbesPerFrame);
        Assert.Equal(0, cfg.WorldProbeClipmap.UploadBudgetBytesPerFrame);
    }
    #endregion
}
