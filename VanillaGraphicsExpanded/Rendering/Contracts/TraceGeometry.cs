namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region Shared geometry
    /// <summary>Declares the coherent geometry sampling resources for surface-cache compute programs.</summary>
    private static void TraceGeometry(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("LumOnNearFieldUBO", GpuBindingRegistry.Ubo.Material);
        contract.RegisterSamplerUnit("nearFieldGeometry", 8);
        contract.RegisterSamplerUnit("nearFieldRegions", 9);
        contract.RegisterSamplerUnit("traceSceneLegacy", 10, required: false);
        contract.RegisterSamplerUnit("traceSceneFaces", 11);
    }
    #endregion
}
