using VanillaGraphicsExpanded.LumOn;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

public sealed class LumonSceneConfigTests
{
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

        Assert.True(cfg.LumOn.LumonScene.TraceScene.ClipmapIssueBudgetMs >= 0f);
        Assert.True(cfg.LumOn.LumonScene.TraceScene.ClipmapDispatchBudgetMs >= 0f);
        Assert.True(cfg.LumOn.LumonScene.TraceScene.ClipmapMaxInFlightRegions >= 0);
    }
}
