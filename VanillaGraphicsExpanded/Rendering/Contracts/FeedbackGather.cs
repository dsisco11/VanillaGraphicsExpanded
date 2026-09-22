namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region FeedbackGather
    /// <summary>Declares the lumonscene_feedback_gather resource slots.</summary>
    private static void FeedbackGather(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgeLumOnSceneFeedbackGatherParamsUBO", GpuBindingRegistry.Ubo.Object);
        contract.RegisterSamplerUnit("vge_patchIdGBuffer", 0);
        contract.RegisterShaderStorageBlockBinding("VgePageRequests", 0);
    }
    #endregion
}
