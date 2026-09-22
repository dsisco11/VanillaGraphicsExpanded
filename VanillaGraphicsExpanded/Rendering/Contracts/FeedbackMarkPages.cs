namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region FeedbackMarkPages
    /// <summary>Declares the lumonscene_feedback_mark_pages resource slots.</summary>
    private static void FeedbackMarkPages(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgeLumOnSceneFeedbackMarkParamsUBO", GpuBindingRegistry.Ubo.Object);
        contract.RegisterSamplerUnit("vge_patchIdGBuffer", 0);
        contract.RegisterSamplerUnit("vge_chunkSlotGenerationTex", 1, required: false);
        contract.RegisterImageUnit("vge_pageUsageStamp", 0);
    }
    #endregion
}
