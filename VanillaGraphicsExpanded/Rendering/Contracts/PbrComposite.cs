namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region PbrComposite
    /// <summary>Declares the pbr_composite resource slots.</summary>
    private static void PbrComposite(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgePbrCompositeParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        contract.RegisterSamplerUnit("directDiffuse", unit: 0, required: true);
        contract.RegisterSamplerUnit("directSpecular", unit: 1, required: true);
        contract.RegisterSamplerUnit("emissive", unit: 2, required: true);
        contract.RegisterSamplerUnit("indirectDiffuse", unit: 3, required: true);
        contract.RegisterSamplerUnit("gBufferAlbedo", unit: 4, required: true);
        contract.RegisterSamplerUnit("gBufferMaterial", unit: 5, required: true);
        contract.RegisterSamplerUnit("primaryDepth", unit: 6, required: true);
        contract.RegisterSamplerUnit("gBufferNormal", unit: 7, required: true);
    }
    #endregion
}
