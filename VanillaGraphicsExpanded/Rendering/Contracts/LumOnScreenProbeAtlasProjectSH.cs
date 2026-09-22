namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnScreenProbeAtlasProjectSH
    /// <summary>Declares the lumon_probe_atlas_project_sh resource slots.</summary>
    private static void LumOnScreenProbeAtlasProjectSH(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterSamplerUnit("octahedralAtlas", 0, required: true);
        contract.RegisterSamplerUnit("probeAtlasMeta", 1, required: true);
        contract.RegisterSamplerUnit("probeAnchorPosition", 2, required: true);
    }
    #endregion
}
