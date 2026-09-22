namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region CaptureMeshCard
    /// <summary>Declares the lumonscene_capture_meshcard resource slots.</summary>
    private static void CaptureMeshCard(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgeLumOnSceneCaptureMeshCardParamsUBO", GpuBindingRegistry.Ubo.Object);
        contract.RegisterImageUnit("vge_depthAtlas", 0);
        contract.RegisterImageUnit("vge_materialAtlas", 1);
        contract.RegisterShaderStorageBlockBinding("VgeMeshCardCaptureWork", 0);
        contract.RegisterShaderStorageBlockBinding("VgePatchMetadataBuffer", 1);
        contract.RegisterShaderStorageBlockBinding("VgeTriangles", 2);
    }
    #endregion
}
