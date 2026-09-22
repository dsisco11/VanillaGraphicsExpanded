namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnScreenProbeAtlasTemporal
    /// <summary>Declares the lumon_probe_atlas_temporal resource slots.</summary>
    private static void LumOnScreenProbeAtlasTemporal(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterUniformBlockBinding("VgeLumOnProbeParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        contract.RegisterSamplerUnit("octahedralCurrent", 0, required: true);
        contract.RegisterSamplerUnit("octahedralHistory", 1, required: true);
        contract.RegisterSamplerUnit("probeAnchorPosition", 2, required: true);
        contract.RegisterSamplerUnit("probeAtlasMetaCurrent", 3, required: true);
        contract.RegisterSamplerUnit("probeAtlasMetaHistory", 4, required: true);
        contract.RegisterSamplerUnit("velocityTex", 5, required: true);
        contract.RegisterSamplerUnit("pmjJitter", 6, required: true);
        contract.RegisterSamplerUnit("probeTraceMask", 7, required: false);
    }
    #endregion
}
