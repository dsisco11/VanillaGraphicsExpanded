namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnGather
    /// <summary>Declares the lumon_probe_atlas_gather resource slots.</summary>
    private static void LumOnGather(GpuBindingContract contract)
    {
        contract.RegisterSamplerUnit("octahedralAtlas", 0, required: true);
        contract.RegisterSamplerUnit("probeAnchorPosition", 1, required: true);
        contract.RegisterSamplerUnit("probeAnchorNormal", 2, required: true);
        contract.RegisterSamplerUnit("primaryDepth", 3, required: true);
        contract.RegisterSamplerUnit("gBufferNormal", 4, required: true);
        contract.RegisterSamplerUnit("worldProbeRadianceAtlas", 5, required: false);
        contract.RegisterSamplerUnit("worldProbeVis0", 8, required: false);
        contract.RegisterSamplerUnit("worldProbeMeta0", 9, required: false);
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterUniformBlockBinding("VgeLumOnProbeParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        contract.RegisterUniformBlockBinding("LumOnNearFieldUBO", GpuBindingRegistry.Ubo.Material, required: false);
        contract.RegisterSamplerUnit("nearFieldGeometry", 6, required: false);
        contract.RegisterSamplerUnit("nearFieldRegions", 7, required: false);
    }
    #endregion
}
