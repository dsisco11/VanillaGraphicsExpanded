using VanillaGraphicsExpanded.LumOn;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

/// <summary>Checks persisted surface-cache configuration and sanitization boundaries.</summary>
public sealed class LumonSceneConfigTests
{
    #region Temporal configuration
    /// <summary>Existing settings without the new field receive the default; explicit values survive JSON persistence.</summary>
    [Fact]
    public void HistoryLimitDefaultsToFourAndRoundTrips()
    {
        var restored=Newtonsoft.Json.JsonConvert.DeserializeObject<VgeConfig>("{\"LumOn\":{\"LumonScene\":{}}}")!;
        Assert.Equal(4,restored.LumOn.LumonScene.RelightMaxFramesAccumulated);
        restored.LumOn.LumonScene.RelightMaxFramesAccumulated=17;
        var copy=Newtonsoft.Json.JsonConvert.DeserializeObject<VgeConfig>(Newtonsoft.Json.JsonConvert.SerializeObject(restored))!;
        Assert.Equal(17,copy.LumOn.LumonScene.RelightMaxFramesAccumulated);
    }

    /// <summary>Invalid persisted counts are clamped without altering legal limits.</summary>
    [Theory]
    [InlineData(-1,1)] [InlineData(0,1)] [InlineData(1,1)] [InlineData(4,4)] [InlineData(255,255)] [InlineData(256,255)]
    public void HistoryLimitIsSanitized(int requested,int expected)
    {
        var config=new VgeConfig();
        config.LumOn.LumonScene.RelightMaxFramesAccumulated=requested;
        config.Sanitize();
        Assert.Equal(expected,config.LumOn.LumonScene.RelightMaxFramesAccumulated);
    }
    #endregion

    /// <summary>Sanitization keeps surface coverage and trace budgets within supported ranges.</summary>
    [Fact]
    public void Sanitize_ClampsLumonSceneSurfaceCacheSettings_AndEnsuresFarRadiusAtLeastNear()
    {
        var cfg = new VgeConfig
        {
            LumOn = new VgeConfig.LumOnSettingsConfig
            {
                LumonScene = new VgeConfig.LumOnSettingsConfig.LumonSceneConfig
                {
                    NearTexelsPerVoxelFaceEdge = -5,
                    FarTexelsPerVoxelFaceEdge = 999,
                    NearRadiusChunks = 12345,
                    NearRadiusYChunks = 12345,
                    FarRadiusChunks = -1,
                    FarRadiusYChunks = -1,
                    TraceScene = new VgeConfig.LumOnSettingsConfig.LumonSceneConfig.TraceSceneConfig
                    {
                        ClipmapRefreshBudgetMs = -1f,
                        ClipmapIssueBudgetMs = -1f,
                        ClipmapDispatchBudgetMs = -1f,
                        ClipmapMaxInFlightRegions = -1,
                    }
                }
            }
        };

        cfg.Sanitize();

        Assert.Equal(1, cfg.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge);
        Assert.Equal(64, cfg.LumOn.LumonScene.FarTexelsPerVoxelFaceEdge);

        Assert.InRange(cfg.LumOn.LumonScene.NearRadiusChunks, 0, 128);
        Assert.InRange(cfg.LumOn.LumonScene.NearRadiusYChunks, 0, 128);
        Assert.InRange(cfg.LumOn.LumonScene.FarRadiusChunks, 0, 128);
        Assert.InRange(cfg.LumOn.LumonScene.FarRadiusYChunks, 0, 128);
        Assert.True(cfg.LumOn.LumonScene.FarRadiusChunks >= cfg.LumOn.LumonScene.NearRadiusChunks);
        Assert.True(cfg.LumOn.LumonScene.FarRadiusYChunks >= cfg.LumOn.LumonScene.NearRadiusYChunks);

        Assert.True(cfg.LumOn.LumonScene.TraceScene.ClipmapRefreshBudgetMs >= 0f);
        Assert.True(cfg.LumOn.LumonScene.TraceScene.ClipmapIssueBudgetMs >= 0f);
        Assert.True(cfg.LumOn.LumonScene.TraceScene.ClipmapDispatchBudgetMs >= 0f);
        Assert.True(cfg.LumOn.LumonScene.TraceScene.ClipmapMaxInFlightRegions >= 0);
    }
}
