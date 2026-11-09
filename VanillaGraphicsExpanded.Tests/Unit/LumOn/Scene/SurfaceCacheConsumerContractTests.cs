using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Records which shader contracts can consume the surface-cache resources.</summary>
public sealed class SurfaceCacheConsumerContractTests
{
    private const string PageTable = "vge_lumonScenePageTableMip0";
    private const string IrradianceAtlas = "vge_lumonSceneIrradianceAtlas";

    #region Public API
    /// <summary>Confirms the diagnostic shader can inspect both cache addressing and cached irradiance.</summary>
    [Fact]
    public void DebugShaderDeclaresSurfaceCacheInputs()
    {
        // Arrange the diagnostic shader identity whose binding contract is under inspection.
        // Act by constructing its generated binding contract.
        GpuBindingContract debug = GpuShaderContracts.Create("lumon_debug_view_lumon_scene_irradiance");
        // Assert the diagnostic contract declares both page-table addressing and irradiance atlas samplers.
        Assert.Contains(PageTable, debug.Samplers.Keys);
        Assert.Contains(IrradianceAtlas, debug.Samplers.Keys);
    }

    /// <summary>Ray-hit consumers bind outgoing radiance; gather and composition remain downstream of probe radiance.</summary>
    [Fact]
    public void HitConsumersDeclareCacheWhileGatherAndCombineDoNot()
    {
        // Arrange the two cache-reading consumers and three downstream consumers by shader identity.
        // Act by resolving their binding contracts and compare the cache samplers/storage declarations.
        // Assert hit consumers declare all cache dependencies while gather/combine omit prior outgoing radiance.
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











