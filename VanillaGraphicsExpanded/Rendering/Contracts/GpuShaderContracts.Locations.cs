namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Stable interface locations shared by independently compiled shader stages.</summary>
internal static partial class GpuShaderContracts
{
    #region Interface locations
    /// <summary>Declares fixed locations; adding a shader never renumbers existing shader interfaces.</summary>
    private static void DeclareLocations(GpuBindingContract contract)
    {
        // Uniform locations and resource binding points are independent namespaces.
        // These explicit locations also allow shared stages to link with multiple programs.
        contract.UniformLocations.Add("baseAlbedoAtlas", 0);
        contract.UniformLocations.Add("directDiffuse", 1);
        contract.UniformLocations.Add("directSpecular", 2);
        contract.UniformLocations.Add("emissive", 3);
        contract.UniformLocations.Add("gBufferAlbedo", 4);
        contract.UniformLocations.Add("gBufferMaterial", 5);
        contract.UniformLocations.Add("gBufferNormal", 6);
        contract.UniformLocations.Add("gBufferPatchId", 7);
        contract.UniformLocations.Add("historyMeta", 8);
        contract.UniformLocations.Add("hzbDepth", 9);
        contract.UniformLocations.Add("importanceColorMode", 10);
        contract.UniformLocations.Add("indirectDiffuse", 11);
        contract.UniformLocations.Add("indirectDiffuseFull", 12);
        contract.UniformLocations.Add("indirectHalf", 13);
        contract.UniformLocations.Add("irradianceAtlas", 14);
        contract.UniformLocations.Add("materialParams", 15);
        contract.UniformLocations.Add("nearFieldGeometry", 16);
        contract.UniformLocations.Add("nearFieldLight", 17);
        contract.UniformLocations.Add("nearFieldMaterials", 18);
        contract.UniformLocations.Add("nearFieldRegions", 19);
        contract.UniformLocations.Add("octahedralAtlas", 20);
        contract.UniformLocations.Add("octahedralCurrent", 21);
        contract.UniformLocations.Add("octahedralHistory", 22);
        contract.UniformLocations.Add("outImg", 23);
        contract.UniformLocations.Add("pmjJitter", 24);
        contract.UniformLocations.Add("primaryDepth", 25);
        contract.UniformLocations.Add("primaryScene", 26);
        contract.UniformLocations.Add("probeAnchorNormal", 27);
        contract.UniformLocations.Add("probeAnchorPosition", 28);
        contract.UniformLocations.Add("probeAtlasCurrent", 29);
        contract.UniformLocations.Add("probeAtlasFiltered", 30);
        contract.UniformLocations.Add("probeAtlasGatherInput", 31);
        contract.UniformLocations.Add("probeAtlasMeta", 32);
        contract.UniformLocations.Add("probeAtlasMetaCurrent", 33);
        contract.UniformLocations.Add("probeAtlasMetaHistory", 34);
        contract.UniformLocations.Add("probeAtlasTrace", 35);
        contract.UniformLocations.Add("probePisEnergy", 36);
        contract.UniformLocations.Add("probeSh0", 37);
        contract.UniformLocations.Add("probeSh1", 38);
        contract.UniformLocations.Add("probeSh2", 39);
        contract.UniformLocations.Add("probeSh3", 40);
        contract.UniformLocations.Add("probeSh4", 41);
        contract.UniformLocations.Add("probeSh5", 42);
        contract.UniformLocations.Add("probeSh6", 43);
        contract.UniformLocations.Add("probeTraceMask", 44);
        contract.UniformLocations.Add("radianceTexture0", 45);
        contract.UniformLocations.Add("radianceTexture1", 46);
        contract.UniformLocations.Add("sceneDirect", 47);
        contract.UniformLocations.Add("shadowMapFar", 48);
        contract.UniformLocations.Add("shadowMapNear", 49);
        contract.UniformLocations.Add("surfaceAlbedo", 50);
        contract.UniformLocations.Add("traceSceneFaces", 51);
        contract.UniformLocations.Add("traceSceneLegacy", 52);
        contract.UniformLocations.Add("uOcc", 53);
        contract.UniformLocations.Add("u_a", 54);
        contract.UniformLocations.Add("u_albedoAtlas", 55);
        contract.UniformLocations.Add("u_atlas", 56);
        contract.UniformLocations.Add("u_b", 57);
        contract.UniformLocations.Add("u_coarseE", 58);
        contract.UniformLocations.Add("u_d", 59);
        contract.UniformLocations.Add("u_fine", 60);
        contract.UniformLocations.Add("u_fineH", 61);
        contract.UniformLocations.Add("u_g", 62);
        contract.UniformLocations.Add("u_g1", 63);
        contract.UniformLocations.Add("u_g2", 64);
        contract.UniformLocations.Add("u_g3", 65);
        contract.UniformLocations.Add("u_g4", 66);
        contract.UniformLocations.Add("u_h", 67);
        contract.UniformLocations.Add("u_height", 68);
        contract.UniformLocations.Add("u_src", 69);
        contract.UniformLocations.Add("velocityTex", 70);
        contract.UniformLocations.Add("vge_blockLevelScalarLut", 71);
        contract.UniformLocations.Add("vge_chunkSlotGenerationTex", 72);
        contract.UniformLocations.Add("vge_depthAtlas", 73);
        contract.UniformLocations.Add("vge_irradianceAtlas", 74);
        contract.UniformLocations.Add("vge_lightColorLut", 75);
        contract.UniformLocations.Add("vge_lumonSceneIrradianceAtlas", 76);
        contract.UniformLocations.Add("vge_lumonSceneMaterialAtlas", 77);
        contract.UniformLocations.Add("vge_lumonScenePageTableMip0", 78);
        contract.UniformLocations.Add("vge_lumonSceneSurfaceLut", 79);
        contract.UniformLocations.Add("vge_materialAtlas", 80);
        contract.UniformLocations.Add("vge_pageTableMip0", 81);
        contract.UniformLocations.Add("vge_pageUsageStamp", 82);
        contract.UniformLocations.Add("vge_patchIdGBuffer", 83);
        contract.UniformLocations.Add("vge_sunLevelScalarLut", 84);
        contract.UniformLocations.Add("vge_surfaceLut", 85);
        contract.UniformLocations.Add("worldProbeDebugState0", 86);
        contract.UniformLocations.Add("worldProbeDist0", 87);
        contract.UniformLocations.Add("worldProbeMeta0", 88);
        contract.UniformLocations.Add("worldProbeRadianceAtlas", 89);
        contract.UniformLocations.Add("worldProbeSuppressedLighting", 90);
        contract.UniformLocations.Add("worldProbeVis0", 91);

        contract.VaryingLocations.Add("uv", 0);
        contract.VaryingLocations.Add("v_uv", 0);
        contract.VaryingLocations.Add("vTexCoord", 0);
        contract.VaryingLocations.Add("vColor", 0);
        contract.VaryingLocations.Add("vAtlasCoord", 1);
        contract.VaryingLocations.Add("vRadiance", 0);
        contract.VaryingLocations.Add("vAoDirWorld", 0);
        contract.VaryingLocations.Add("vAoConfidence", 1);
        contract.VaryingLocations.Add("vConfidence", 2);
        contract.VaryingLocations.Add("vMeanLogHitDistance", 3);
        contract.VaryingLocations.Add("vSkyIntensity", 4);
        contract.VaryingLocations.Add("vFlags", 5);
        contract.FragmentOutputLocations.Add("outColor", 0);
        contract.FragmentOutputLocations.Add("fragColor", 0);
        contract.FragmentOutputLocations.Add("color", 0);
    }
    #endregion
}
