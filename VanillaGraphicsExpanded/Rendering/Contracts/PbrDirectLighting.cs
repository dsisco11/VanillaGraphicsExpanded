namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region PbrDirectLighting
    /// <summary>Declares the pbr_direct_lighting resource slots.</summary>
    private static void PbrDirectLighting(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgePbrDirectLightingParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        contract.RegisterSamplerUnit("primaryScene", unit: 0, required: true);
        contract.RegisterSamplerUnit("primaryDepth", unit: 1, required: true);
        contract.RegisterSamplerUnit("gBufferNormal", unit: 2, required: true);
        contract.RegisterSamplerUnit("gBufferMaterial", unit: 3, required: true);
        contract.RegisterSamplerUnit("shadowMapNear", unit: 4, required: true);
        contract.RegisterSamplerUnit("shadowMapFar", unit: 5, required: true);
    }
    #endregion
}
