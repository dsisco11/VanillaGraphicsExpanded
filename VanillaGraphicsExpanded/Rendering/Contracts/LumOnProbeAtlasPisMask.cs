namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnProbeAtlasPisMask
    /// <summary>Declares the lumon_probe_atlas_pis_mask resource slots.</summary>
    private static void LumOnProbeAtlasPisMask(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterSamplerUnit("probeAnchorPosition", 0, required: true);
        contract.RegisterSamplerUnit("probeAnchorNormal", 1, required: true);
        contract.RegisterSamplerUnit("octahedralHistory", 2, required: true);
        contract.RegisterSamplerUnit("probeAtlasMetaHistory", 3, required: true);
    }
    #endregion
}
