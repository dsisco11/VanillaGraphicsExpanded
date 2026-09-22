namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnSh9Gather
    /// <summary>Declares the lumon_probe_sh9_gather resource slots.</summary>
    private static void LumOnSh9Gather(GpuBindingContract contract)
    {
        contract.RegisterSamplerUnit("probeSh0", 0, required: true);
        contract.RegisterSamplerUnit("probeSh1", 1, required: true);
        contract.RegisterSamplerUnit("probeSh2", 2, required: true);
        contract.RegisterSamplerUnit("probeSh3", 3, required: true);
        contract.RegisterSamplerUnit("probeSh4", 4, required: true);
        contract.RegisterSamplerUnit("probeSh5", 5, required: true);
        contract.RegisterSamplerUnit("probeSh6", 6, required: true);
        contract.RegisterSamplerUnit("probeAnchorPosition", 7, required: true);
        contract.RegisterSamplerUnit("probeAnchorNormal", 8, required: true);
        contract.RegisterSamplerUnit("primaryDepth", 9, required: true);
        contract.RegisterSamplerUnit("gBufferNormal", 10, required: true);
        contract.RegisterSamplerUnit("worldProbeRadianceAtlas", 11, required: false);
        contract.RegisterSamplerUnit("worldProbeVis0", 14, required: false);
        contract.RegisterSamplerUnit("worldProbeMeta0", 15, required: false);
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterUniformBlockBinding("VgeLumOnProbeParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        contract.RegisterUniformBlockBinding("LumOnNearFieldUBO", GpuBindingRegistry.Ubo.Material, required: false);
        contract.RegisterSamplerUnit("nearFieldGeometry", 12, required: false);
        contract.RegisterSamplerUnit("nearFieldRegions", 13, required: false);
    }
    #endregion
}
