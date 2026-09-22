namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnProbeAnchor
    /// <summary>Declares the lumon_probe_anchor resource slots.</summary>
    private static void LumOnProbeAnchor(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterUniformBlockBinding("VgeLumOnProbeParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        contract.RegisterSamplerUnit("primaryDepth", 0, required: true);
        contract.RegisterSamplerUnit("gBufferNormal", 1, required: true);
        contract.RegisterSamplerUnit("pmjJitter", 2, required: true);
    }
    #endregion
}
