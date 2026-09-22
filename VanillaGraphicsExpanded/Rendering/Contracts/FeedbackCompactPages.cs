namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region FeedbackCompactPages
    /// <summary>Declares the lumonscene_feedback_compact_pages resource slots.</summary>
    private static void FeedbackCompactPages(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgeLumOnSceneFeedbackCompactParamsUBO", GpuBindingRegistry.Ubo.Object);
        contract.RegisterSamplerUnit("vge_pageUsageStamp", 0);
        contract.RegisterSamplerUnit("vge_pageTableMip0", 1);
        contract.RegisterShaderStorageBlockBinding("VgePageRequests", 0);
    }
    #endregion
}
