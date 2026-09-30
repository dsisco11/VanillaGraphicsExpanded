namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared offline and runtime liquid resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region Liquid bindings
    /// <summary>Declares both parameter blocks and every sampled image explicitly.</summary>
    private static void Liquid(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgeLiquidFrameParams", GpuBindingRegistry.Ubo.Frame);
        contract.RegisterUniformBlockBinding("VgeLiquidDrawParams", GpuBindingRegistry.Ubo.Object);
        contract.RegisterSamplerUnit("terrainTex", 0);
        contract.UniformLocations["terrainTex"] = 100;
        contract.RegisterSamplerUnit("depthTex", 1);
        contract.UniformLocations["depthTex"] = 101;
        contract.RegisterSamplerUnit("vge_materialParamsTex", 2);
        contract.UniformLocations["vge_materialParamsTex"] = 102;
        contract.RegisterSamplerUnit("shadowMapNear", 3);
        contract.UniformLocations["shadowMapNear"] = 49;
        contract.RegisterSamplerUnit("shadowMapFar", 4);
        contract.UniformLocations["shadowMapFar"] = 48;
        contract.RegisterSamplerUnit("vge_atmosphereAerialRadiance", 5);
        contract.UniformLocations["vge_atmosphereAerialRadiance"] = 93;
        contract.RegisterSamplerUnit("vge_atmosphereAerialAttenuation", 6);
        contract.UniformLocations["vge_atmosphereAerialAttenuation"] = 94;
    }
    #endregion
}
