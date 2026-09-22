namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnUpsample
    /// <summary>Declares the lumon_upsample resource slots.</summary>
    private static void LumOnUpsample(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterUniformBlockBinding("VgeLumOnUpsampleParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        contract.RegisterSamplerUnit("indirectHalf", 0, required: true);
        contract.RegisterSamplerUnit("primaryDepth", 1, required: true);
        contract.RegisterSamplerUnit("gBufferNormal", 2, required: true);
    }
    #endregion
}
