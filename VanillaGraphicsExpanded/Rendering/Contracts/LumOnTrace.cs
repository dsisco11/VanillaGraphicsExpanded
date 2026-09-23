namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnTrace
    /// <summary>Declares the lumon_probe_atlas_trace resource slots.</summary>
    private static void LumOnTrace(GpuBindingContract contract)
    {
        SurfaceLightingInputs(contract);
        contract.RegisterSamplerUnit("traceSceneFaces", 19, required: false);
        contract.RegisterSamplerUnit("probeAnchorPosition", 0, required: true);
        contract.RegisterSamplerUnit("probeAnchorNormal", 1, required: true);
        contract.RegisterSamplerUnit("primaryDepth", 2, required: true);
        contract.RegisterSamplerUnit("surfaceAlbedo", 3, required: true);
        contract.RegisterSamplerUnit("gBufferMaterial", 4, required: true);
        contract.RegisterSamplerUnit("octahedralHistory", 5, required: true);
        contract.RegisterSamplerUnit("hzbDepth", 6, required: true);
        contract.RegisterSamplerUnit("probeAtlasMetaHistory", 7, required: true);
        contract.RegisterSamplerUnit("worldProbeRadianceAtlas", 8, required: false);
        contract.RegisterSamplerUnit("probeTraceMask", 9, required: true);
        contract.RegisterSamplerUnit("worldProbeVis0", 11, required: false);
        contract.RegisterSamplerUnit("worldProbeMeta0", 12, required: false);
        contract.RegisterSamplerUnit("nearFieldGeometry", 10, required: false);
        contract.RegisterSamplerUnit("nearFieldLight", 13, required: false);
        contract.RegisterSamplerUnit("nearFieldRegions", 14, required: false);
        contract.RegisterSamplerUnit("nearFieldMaterials", 15, required: false);
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterUniformBlockBinding("LumOnWorldProbeUBO", GpuBindingRegistry.Ubo.WorldProbe, required: false);
        contract.RegisterUniformBlockBinding("LumOnNearFieldUBO", GpuBindingRegistry.Ubo.Material, required: false);
        contract.RegisterUniformBlockBinding("VgeLumOnProbeParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
    }
    #endregion
}
