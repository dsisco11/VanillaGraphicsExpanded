using VanillaGraphicsExpanded.LumOn;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn;

public sealed class WorldProbeClipmapConfigTests
{
    [Fact]
    public void Sanitize_ClampsWorldProbeClipmapConfig_AndResizesBudgets()
    {
        var cfg = new VgeConfig
        {
            WorldProbeClipmap = new VgeConfig.WorldProbeClipmapConfig
            {
                ClipmapBaseSpacing = -123f,
                ClipmapResolution = 1,
                ClipmapLevels = 7,
                PerLevelProbeUpdateBudget = [1000],
                TraceMaxProbesPerFrame = -5,
                UploadBudgetBytesPerFrame = -1
            }
        };

        cfg.Sanitize();

        Assert.InRange(cfg.WorldProbeClipmap.ClipmapBaseSpacing, 0.25f, 64f);
        Assert.InRange(cfg.WorldProbeClipmap.ClipmapResolution, 8, 128);
        Assert.InRange(cfg.WorldProbeClipmap.ClipmapLevels, 1, 8);
        Assert.Equal(cfg.WorldProbeClipmap.ClipmapLevels, cfg.WorldProbeClipmap.PerLevelProbeUpdateBudget.Length);
        Assert.Equal(0, cfg.WorldProbeClipmap.TraceMaxProbesPerFrame);
        Assert.Equal(0, cfg.WorldProbeClipmap.UploadBudgetBytesPerFrame);
    }
}
