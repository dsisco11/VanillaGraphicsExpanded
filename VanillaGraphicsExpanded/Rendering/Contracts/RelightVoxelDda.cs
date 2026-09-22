namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region RelightVoxelDda
    /// <summary>Declares the lumonscene_relight_voxel_dda resource slots.</summary>
    private static void RelightVoxelDda(GpuBindingContract contract)
    {
        TraceGeometry(contract);
        contract.RegisterUniformBlockBinding("VgeLumOnSceneRelightParamsUBO", GpuBindingRegistry.Ubo.Object);

        contract.RegisterSamplerUnit("vge_depthAtlas", 0);
        contract.RegisterSamplerUnit("vge_materialAtlas", 1);
        contract.RegisterSamplerUnit("vge_lightColorLut", 3);
        contract.RegisterSamplerUnit("vge_blockLevelScalarLut", 4);
        contract.RegisterSamplerUnit("vge_sunLevelScalarLut", 5);
        contract.RegisterSamplerUnit("vge_surfaceLut", 7);

        contract.RegisterImageUnit("vge_irradianceAtlas", 0);

        contract.RegisterShaderStorageBlockBinding("VgeRelightWork", 0);
        contract.RegisterShaderStorageBlockBinding("VgePatchMetadata", 1);
    }
    #endregion
}
