using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

/// <summary>Records which shader contracts can consume the surface-cache resources.</summary>
public sealed class SurfaceCacheConsumerContractTests
{
    private const string PageTable = "vge_lumonScenePageTableMip0";
    private const string IrradianceAtlas = "vge_lumonSceneIrradianceAtlas";

    #region Current consumer inventory
    /// <summary>Confirms the diagnostic shader can inspect both cache addressing and cached irradiance.</summary>
    [Fact]
    public void DebugShaderDeclaresSurfaceCacheInputs()
    {
        GpuBindingContract debug = GpuShaderContracts.Create("lumon_debug");

        Assert.Contains(PageTable, debug.Samplers.Keys);
        Assert.Contains(IrradianceAtlas, debug.Samplers.Keys);
    }

    /// <summary>Documents the missing production link that prevents cached irradiance from reaching final lighting.</summary>
    [Fact]
    public void FinalLightingShadersDoNotYetDeclareSurfaceCacheInputs()
    {
        string[] productionConsumers =
        [
            "lumon_probe_atlas_trace",
            "lumon_probe_atlas_gather",
            "lumon_probe_sh9_gather",
            "lumon_combine"
        ];

        foreach (string identity in productionConsumers)
        {
            GpuShaderContract program = GpuShaderContracts.Registry.FindProgram(identity);
            Assert.All(program.Stages, stage =>
            {
                Assert.DoesNotContain(PageTable, stage.Bindings.Samplers.Keys);
                Assert.DoesNotContain(IrradianceAtlas, stage.Bindings.Samplers.Keys);
            });
        }
    }
    #endregion
}
