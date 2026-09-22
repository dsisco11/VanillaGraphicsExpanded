namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region CaptureVoxel
    /// <summary>Declares the lumonscene_capture_voxel resource slots.</summary>
    private static void CaptureVoxel(GpuBindingContract contract)
    {
        TraceGeometry(contract);
        contract.RegisterUniformBlockBinding("VgeLumOnSceneCaptureVoxelParamsUBO", GpuBindingRegistry.Ubo.Object);
        contract.RegisterImageUnit("vge_depthAtlas", 0);
        contract.RegisterImageUnit("vge_materialAtlas", 1);
        contract.RegisterShaderStorageBlockBinding("VgeCaptureWork", 0);
        contract.RegisterShaderStorageBlockBinding("VgePatchMetadata", 1);
        contract.RegisterShaderStorageBlockBinding("VgeChunkSlotInfo", 2);
    }
    #endregion
}
