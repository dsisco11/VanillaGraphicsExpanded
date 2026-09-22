namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnHzbDownsample
    /// <summary>Declares the lumon_hzb_downsample resource slots.</summary>
    private static void LumOnHzbDownsample(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgeLumOnHzbDownsampleParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        contract.RegisterSamplerUnit("hzbDepth", 0, required: true);
    }
    #endregion
}
