namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnDebug
    /// <summary>Declares the lumon_debug resource slots.</summary>
    private static void LumOnDebug(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterUniformBlockBinding("LumOnWorldProbeUBO", GpuBindingRegistry.Ubo.WorldProbe, required: false);
        contract.RegisterUniformBlockBinding("LumOnTerrainBridgeUBO", GpuBindingRegistry.Ubo.TerrainBridge, required: false);
        contract.RegisterUniformBlockBinding("VgeLumOnDebugParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
        contract.RegisterSamplerUnit("primaryDepth", 0, required: true);
        contract.RegisterSamplerUnit("gBufferNormal", 1, required: true);
        contract.RegisterSamplerUnit("probeAnchorPosition", 2, required: false);
        contract.RegisterSamplerUnit("probeAnchorNormal", 3, required: false);
        contract.RegisterSamplerUnit("radianceTexture0", 4, required: false);
        contract.RegisterSamplerUnit("radianceTexture1", 5, required: false);
        contract.RegisterSamplerUnit("indirectHalf", 6, required: false);
        contract.RegisterSamplerUnit("historyMeta", 7, required: false);
        contract.RegisterSamplerUnit("probeAtlasMeta", 8, required: false);
        contract.RegisterSamplerUnit("probeAtlasCurrent", 9, required: false);
        contract.RegisterSamplerUnit("probeAtlasFiltered", 10, required: false);
        contract.RegisterSamplerUnit("probeAtlasGatherInput", 11, required: false);
        contract.RegisterSamplerUnit("indirectDiffuseFull", 12, required: false);
        contract.RegisterSamplerUnit("gBufferAlbedo", 13, required: false);
        contract.RegisterSamplerUnit("gBufferMaterial", 14, required: false);
        contract.RegisterSamplerUnit("directDiffuse", 15, required: false);
        contract.RegisterSamplerUnit("directSpecular", 16, required: false);
        contract.RegisterSamplerUnit("emissive", 17, required: false);
        contract.RegisterSamplerUnit("velocityTex", 18, required: false);
        contract.RegisterSamplerUnit("worldProbeRadianceAtlas", 19, required: false);
        contract.RegisterSamplerUnit("traceSceneLegacy", 20, required: false);
        contract.RegisterSamplerUnit("worldProbeSuppressedLighting", 21, required: false);
        contract.RegisterSamplerUnit("worldProbeVis0", 22, required: false);
        contract.RegisterSamplerUnit("worldProbeDist0", 23, required: false);
        contract.RegisterSamplerUnit("worldProbeMeta0", 24, required: false);
        contract.RegisterSamplerUnit("worldProbeDebugState0", 25, required: false);
        contract.RegisterSamplerUnit("probeTraceMask", 26, required: false);
        contract.RegisterSamplerUnit("probeAtlasTrace", 27, required: false);
        contract.RegisterSamplerUnit("probePisEnergy", 28, required: false);
        contract.RegisterSamplerUnit("gBufferPatchId", 29, required: false);
        contract.RegisterSamplerUnit("vge_lumonScenePageTableMip0", 30, required: false);
        contract.RegisterSamplerUnit("vge_lumonSceneIrradianceAtlas", 31, required: false);
        contract.RegisterSamplerUnit("vge_lumonSceneMaterialAtlas", 32, required: false);
        contract.RegisterSamplerUnit("vge_lumonSceneSurfaceLut", 33, required: false);
        contract.RegisterUniformBlockBinding("LumOnNearFieldUBO", GpuBindingRegistry.Ubo.Material, required: false);
        contract.RegisterSamplerUnit("nearFieldGeometry", 34, required: false);
        contract.RegisterSamplerUnit("nearFieldRegions", 35, required: false);
    }
    #endregion
}
