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
        GpuBindingContract debug = GpuShaderContracts.Create("lumon_debug_view_lumon_scene_irradiance");

        Assert.Contains(PageTable, debug.Samplers.Keys);
        Assert.Contains(IrradianceAtlas, debug.Samplers.Keys);
    }

    /// <summary>Ray-hit consumers bind outgoing radiance; gather and composition remain downstream of probe radiance.</summary>
    [Fact]
    public void HitConsumersDeclareCacheWhileGatherAndCombineDoNot()
    {
        foreach(string identity in new[]{"lumon_probe_atlas_trace","lumonscene_surface_query"})
        {
            var contract=GpuShaderContracts.Create(identity);
            Assert.Contains("previousOutgoing",contract.Samplers.Keys);
            Assert.Contains("surfacePages",contract.Samplers.Keys);
            Assert.Contains("SurfaceReady",contract.StorageBlocks.Keys);
        }
        foreach(string identity in new[]{"lumon_probe_atlas_gather","lumon_probe_sh9_gather","lumon_combine"})
            Assert.DoesNotContain("previousOutgoing",GpuShaderContracts.Create(identity).Samplers.Keys);
    }
    #endregion
}
