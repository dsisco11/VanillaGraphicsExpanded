namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnCombine
    /// <summary>Declares the lumon_combine resource slots.</summary>
    private static void LumOnCombine(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterUniformBlockBinding("VgeLumOnCombineParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        contract.RegisterSamplerUnit("sceneDirect", 0, required: true);
        contract.RegisterSamplerUnit("indirectDiffuse", 1, required: true);
        contract.RegisterSamplerUnit("gBufferAlbedo", 2, required: true);
        contract.RegisterSamplerUnit("gBufferMaterial", 3, required: true);
        contract.RegisterSamplerUnit("primaryDepth", 4, required: true);
        contract.RegisterSamplerUnit("gBufferNormal", 5, required: true);
    }
    #endregion
}
