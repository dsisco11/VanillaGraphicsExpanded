namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region PbrHeightBake
    /// <summary>Declares the pbr_heightbake resource slots.</summary>
    private static void PbrHeightBake(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgePbrHeightBakeParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
    }
    #endregion
}
