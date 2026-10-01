using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Owns a shared compile-time GPU interface.</summary>
internal static partial class ShaderInterfaceLocations
{
    #region Private
    /// <summary>Declares the baseAlbedoAtlas UniformLocation slot.</summary>
    [ShaderBinding("baseAlbedoAtlas", ShaderBindingKind.UniformLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding BaseAlbedoAtlasLocation { get; }
    /// <summary>Declares the directDiffuse UniformLocation slot.</summary>
    [ShaderBinding("directDiffuse", ShaderBindingKind.UniformLocation, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding DirectDiffuseLocation { get; }
    /// <summary>Declares the directSpecular UniformLocation slot.</summary>
    [ShaderBinding("directSpecular", ShaderBindingKind.UniformLocation, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding DirectSpecularLocation { get; }
    /// <summary>Declares the emissive UniformLocation slot.</summary>
    [ShaderBinding("emissive", ShaderBindingKind.UniformLocation, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding EmissiveLocation { get; }
    /// <summary>Declares the gBufferAlbedo UniformLocation slot.</summary>
    [ShaderBinding("gBufferAlbedo", ShaderBindingKind.UniformLocation, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding GBufferAlbedoLocation { get; }
    /// <summary>Declares the gBufferMaterial UniformLocation slot.</summary>
    [ShaderBinding("gBufferMaterial", ShaderBindingKind.UniformLocation, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding GBufferMaterialLocation { get; }
    /// <summary>Declares the gBufferNormal UniformLocation slot.</summary>
    [ShaderBinding("gBufferNormal", ShaderBindingKind.UniformLocation, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding GBufferNormalLocation { get; }
    /// <summary>Declares the gBufferPatchId UniformLocation slot.</summary>
    [ShaderBinding("gBufferPatchId", ShaderBindingKind.UniformLocation, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding GBufferPatchIdLocation { get; }
    /// <summary>Declares the historyMeta UniformLocation slot.</summary>
    [ShaderBinding("historyMeta", ShaderBindingKind.UniformLocation, 8, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding HistoryMetaLocation { get; }
    /// <summary>Declares the hzbDepth UniformLocation slot.</summary>
    [ShaderBinding("hzbDepth", ShaderBindingKind.UniformLocation, 9, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding HzbDepthLocation { get; }
    /// <summary>Declares the importanceColorMode UniformLocation slot.</summary>
    [ShaderBinding("importanceColorMode", ShaderBindingKind.UniformLocation, 10, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ImportanceColorModeLocation { get; }
    /// <summary>Declares the indirectDiffuse UniformLocation slot.</summary>
    [ShaderBinding("indirectDiffuse", ShaderBindingKind.UniformLocation, 11, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding IndirectDiffuseLocation { get; }
    /// <summary>Declares the indirectDiffuseFull UniformLocation slot.</summary>
    [ShaderBinding("indirectDiffuseFull", ShaderBindingKind.UniformLocation, 12, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding IndirectDiffuseFullLocation { get; }
    /// <summary>Declares the indirectHalf UniformLocation slot.</summary>
    [ShaderBinding("indirectHalf", ShaderBindingKind.UniformLocation, 13, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding IndirectHalfLocation { get; }
    /// <summary>Declares the irradianceAtlas UniformLocation slot.</summary>
    [ShaderBinding("irradianceAtlas", ShaderBindingKind.UniformLocation, 14, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding IrradianceAtlasLocation { get; }
    /// <summary>Declares the materialParams UniformLocation slot.</summary>
    [ShaderBinding("materialParams", ShaderBindingKind.UniformLocation, 15, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding MaterialParamsLocation { get; }
    /// <summary>Declares the nearFieldGeometry UniformLocation slot.</summary>
    [ShaderBinding("nearFieldGeometry", ShaderBindingKind.UniformLocation, 16, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding NearFieldGeometryLocation { get; }
    /// <summary>Declares the nearFieldLight UniformLocation slot.</summary>
    [ShaderBinding("nearFieldLight", ShaderBindingKind.UniformLocation, 17, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding NearFieldLightLocation { get; }
    /// <summary>Declares the nearFieldMaterials UniformLocation slot.</summary>
    [ShaderBinding("nearFieldMaterials", ShaderBindingKind.UniformLocation, 18, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding NearFieldMaterialsLocation { get; }
    /// <summary>Declares the nearFieldRegions UniformLocation slot.</summary>
    [ShaderBinding("nearFieldRegions", ShaderBindingKind.UniformLocation, 19, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding NearFieldRegionsLocation { get; }
    /// <summary>Declares the octahedralAtlas UniformLocation slot.</summary>
    [ShaderBinding("octahedralAtlas", ShaderBindingKind.UniformLocation, 20, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding OctahedralAtlasLocation { get; }
    /// <summary>Declares the octahedralCurrent UniformLocation slot.</summary>
    [ShaderBinding("octahedralCurrent", ShaderBindingKind.UniformLocation, 21, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding OctahedralCurrentLocation { get; }
    /// <summary>Declares the octahedralHistory UniformLocation slot.</summary>
    [ShaderBinding("octahedralHistory", ShaderBindingKind.UniformLocation, 22, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding OctahedralHistoryLocation { get; }
    /// <summary>Declares the outImg UniformLocation slot.</summary>
    [ShaderBinding("outImg", ShaderBindingKind.UniformLocation, 23, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding OutImgLocation { get; }
    /// <summary>Declares the pmjJitter UniformLocation slot.</summary>
    [ShaderBinding("pmjJitter", ShaderBindingKind.UniformLocation, 24, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding PmjJitterLocation { get; }
    /// <summary>Declares the primaryDepth UniformLocation slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.UniformLocation, 25, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding PrimaryDepthLocation { get; }
    /// <summary>Declares the primaryScene UniformLocation slot.</summary>
    [ShaderBinding("primaryScene", ShaderBindingKind.UniformLocation, 26, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding PrimarySceneLocation { get; }
    /// <summary>Declares the probeAnchorNormal UniformLocation slot.</summary>
    [ShaderBinding("probeAnchorNormal", ShaderBindingKind.UniformLocation, 27, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeAnchorNormalLocation { get; }
    /// <summary>Declares the probeAnchorPosition UniformLocation slot.</summary>
    [ShaderBinding("probeAnchorPosition", ShaderBindingKind.UniformLocation, 28, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeAnchorPositionLocation { get; }
    /// <summary>Declares the probeAtlasCurrent UniformLocation slot.</summary>
    [ShaderBinding("probeAtlasCurrent", ShaderBindingKind.UniformLocation, 29, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeAtlasCurrentLocation { get; }
    /// <summary>Declares the probeAtlasFiltered UniformLocation slot.</summary>
    [ShaderBinding("probeAtlasFiltered", ShaderBindingKind.UniformLocation, 30, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeAtlasFilteredLocation { get; }
    /// <summary>Declares the probeAtlasGatherInput UniformLocation slot.</summary>
    [ShaderBinding("probeAtlasGatherInput", ShaderBindingKind.UniformLocation, 31, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeAtlasGatherInputLocation { get; }
    /// <summary>Declares the probeAtlasMeta UniformLocation slot.</summary>
    [ShaderBinding("probeAtlasMeta", ShaderBindingKind.UniformLocation, 32, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeAtlasMetaLocation { get; }
    /// <summary>Declares the probeAtlasMetaCurrent UniformLocation slot.</summary>
    [ShaderBinding("probeAtlasMetaCurrent", ShaderBindingKind.UniformLocation, 33, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeAtlasMetaCurrentLocation { get; }
    /// <summary>Declares the probeAtlasMetaHistory UniformLocation slot.</summary>
    [ShaderBinding("probeAtlasMetaHistory", ShaderBindingKind.UniformLocation, 34, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeAtlasMetaHistoryLocation { get; }
    /// <summary>Declares the probeAtlasTrace UniformLocation slot.</summary>
    [ShaderBinding("probeAtlasTrace", ShaderBindingKind.UniformLocation, 35, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeAtlasTraceLocation { get; }
    /// <summary>Declares the probePisEnergy UniformLocation slot.</summary>
    [ShaderBinding("probePisEnergy", ShaderBindingKind.UniformLocation, 36, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbePisEnergyLocation { get; }
    /// <summary>Declares the probeSh0 UniformLocation slot.</summary>
    [ShaderBinding("probeSh0", ShaderBindingKind.UniformLocation, 37, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeSh0Location { get; }
    /// <summary>Declares the probeSh1 UniformLocation slot.</summary>
    [ShaderBinding("probeSh1", ShaderBindingKind.UniformLocation, 38, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeSh1Location { get; }
    /// <summary>Declares the probeSh2 UniformLocation slot.</summary>
    [ShaderBinding("probeSh2", ShaderBindingKind.UniformLocation, 39, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeSh2Location { get; }
    /// <summary>Declares the probeSh3 UniformLocation slot.</summary>
    [ShaderBinding("probeSh3", ShaderBindingKind.UniformLocation, 40, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeSh3Location { get; }
    /// <summary>Declares the probeSh4 UniformLocation slot.</summary>
    [ShaderBinding("probeSh4", ShaderBindingKind.UniformLocation, 41, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeSh4Location { get; }
    /// <summary>Declares the probeSh5 UniformLocation slot.</summary>
    [ShaderBinding("probeSh5", ShaderBindingKind.UniformLocation, 42, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeSh5Location { get; }
    /// <summary>Declares the probeSh6 UniformLocation slot.</summary>
    [ShaderBinding("probeSh6", ShaderBindingKind.UniformLocation, 43, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeSh6Location { get; }
    /// <summary>Declares the probeTraceMask UniformLocation slot.</summary>
    [ShaderBinding("probeTraceMask", ShaderBindingKind.UniformLocation, 44, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ProbeTraceMaskLocation { get; }
    /// <summary>Declares the radianceTexture0 UniformLocation slot.</summary>
    [ShaderBinding("radianceTexture0", ShaderBindingKind.UniformLocation, 45, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding RadianceTexture0Location { get; }
    /// <summary>Declares the radianceTexture1 UniformLocation slot.</summary>
    [ShaderBinding("radianceTexture1", ShaderBindingKind.UniformLocation, 46, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding RadianceTexture1Location { get; }
    /// <summary>Declares the sceneDirect UniformLocation slot.</summary>
    [ShaderBinding("sceneDirect", ShaderBindingKind.UniformLocation, 47, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding SceneDirectLocation { get; }
    /// <summary>Declares the shadowMapFar UniformLocation slot.</summary>
    [ShaderBinding("shadowMapFar", ShaderBindingKind.UniformLocation, 48, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ShadowMapFarLocation { get; }
    /// <summary>Declares the shadowMapNear UniformLocation slot.</summary>
    [ShaderBinding("shadowMapNear", ShaderBindingKind.UniformLocation, 49, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ShadowMapNearLocation { get; }
    /// <summary>Declares the surfaceAlbedo UniformLocation slot.</summary>
    [ShaderBinding("surfaceAlbedo", ShaderBindingKind.UniformLocation, 50, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding SurfaceAlbedoLocation { get; }
    /// <summary>Declares the traceSceneFaces UniformLocation slot.</summary>
    [ShaderBinding("traceSceneFaces", ShaderBindingKind.UniformLocation, 51, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding TraceSceneFacesLocation { get; }
    /// <summary>Declares the traceSceneLegacy UniformLocation slot.</summary>
    [ShaderBinding("traceSceneLegacy", ShaderBindingKind.UniformLocation, 52, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding TraceSceneLegacyLocation { get; }
    /// <summary>Declares the uOcc UniformLocation slot.</summary>
    [ShaderBinding("uOcc", ShaderBindingKind.UniformLocation, 53, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UOccLocation { get; }
    /// <summary>Declares the u_a UniformLocation slot.</summary>
    [ShaderBinding("u_a", ShaderBindingKind.UniformLocation, 54, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UALocation { get; }
    /// <summary>Declares the u_albedoAtlas UniformLocation slot.</summary>
    [ShaderBinding("u_albedoAtlas", ShaderBindingKind.UniformLocation, 55, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UAlbedoAtlasLocation { get; }
    /// <summary>Declares the u_atlas UniformLocation slot.</summary>
    [ShaderBinding("u_atlas", ShaderBindingKind.UniformLocation, 56, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UAtlasLocation { get; }
    /// <summary>Declares the u_b UniformLocation slot.</summary>
    [ShaderBinding("u_b", ShaderBindingKind.UniformLocation, 57, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UBLocation { get; }
    /// <summary>Declares the u_coarseE UniformLocation slot.</summary>
    [ShaderBinding("u_coarseE", ShaderBindingKind.UniformLocation, 58, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UCoarseELocation { get; }
    /// <summary>Declares the u_d UniformLocation slot.</summary>
    [ShaderBinding("u_d", ShaderBindingKind.UniformLocation, 59, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UDLocation { get; }
    /// <summary>Declares the u_fine UniformLocation slot.</summary>
    [ShaderBinding("u_fine", ShaderBindingKind.UniformLocation, 60, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UFineLocation { get; }
    /// <summary>Declares the u_fineH UniformLocation slot.</summary>
    [ShaderBinding("u_fineH", ShaderBindingKind.UniformLocation, 61, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UFineHLocation { get; }
    /// <summary>Declares the u_g UniformLocation slot.</summary>
    [ShaderBinding("u_g", ShaderBindingKind.UniformLocation, 62, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UGLocation { get; }
    /// <summary>Declares the u_g1 UniformLocation slot.</summary>
    [ShaderBinding("u_g1", ShaderBindingKind.UniformLocation, 63, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UG1Location { get; }
    /// <summary>Declares the u_g2 UniformLocation slot.</summary>
    [ShaderBinding("u_g2", ShaderBindingKind.UniformLocation, 64, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UG2Location { get; }
    /// <summary>Declares the u_g3 UniformLocation slot.</summary>
    [ShaderBinding("u_g3", ShaderBindingKind.UniformLocation, 65, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UG3Location { get; }
    /// <summary>Declares the u_g4 UniformLocation slot.</summary>
    [ShaderBinding("u_g4", ShaderBindingKind.UniformLocation, 66, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UG4Location { get; }
    /// <summary>Declares the u_h UniformLocation slot.</summary>
    [ShaderBinding("u_h", ShaderBindingKind.UniformLocation, 67, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UHLocation { get; }
    /// <summary>Declares the u_height UniformLocation slot.</summary>
    [ShaderBinding("u_height", ShaderBindingKind.UniformLocation, 68, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding UHeightLocation { get; }
    /// <summary>Declares the u_src UniformLocation slot.</summary>
    [ShaderBinding("u_src", ShaderBindingKind.UniformLocation, 69, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding USrcLocation { get; }
    /// <summary>Declares the velocityTex UniformLocation slot.</summary>
    [ShaderBinding("velocityTex", ShaderBindingKind.UniformLocation, 70, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding VelocityTexLocation { get; }
    /// <summary>Declares the vge_blockLevelScalarLut UniformLocation slot.</summary>
    [ShaderBinding("vge_blockLevelScalarLut", ShaderBindingKind.UniformLocation, 71, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding BlockLevelScalarLutLocation { get; }
    /// <summary>Declares the vge_chunkSlotGenerationTex UniformLocation slot.</summary>
    [ShaderBinding("vge_chunkSlotGenerationTex", ShaderBindingKind.UniformLocation, 72, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding ChunkSlotGenerationTexLocation { get; }
    /// <summary>Declares the vge_depthAtlas UniformLocation slot.</summary>
    [ShaderBinding("vge_depthAtlas", ShaderBindingKind.UniformLocation, 73, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding DepthAtlasLocation { get; }
    /// <summary>Declares the vge_irradianceAtlas UniformLocation slot.</summary>
    [ShaderBinding("vge_irradianceAtlas", ShaderBindingKind.UniformLocation, 74, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding IrradianceAtlasUniformLocation { get; }
    /// <summary>Declares the vge_lightColorLut UniformLocation slot.</summary>
    [ShaderBinding("vge_lightColorLut", ShaderBindingKind.UniformLocation, 75, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding LightColorLutLocation { get; }
    /// <summary>Declares the vge_lumonSceneIrradianceAtlas UniformLocation slot.</summary>
    [ShaderBinding("vge_lumonSceneIrradianceAtlas", ShaderBindingKind.UniformLocation, 76, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding LumonSceneIrradianceAtlasLocation { get; }
    /// <summary>Declares the vge_lumonSceneMaterialAtlas UniformLocation slot.</summary>
    [ShaderBinding("vge_lumonSceneMaterialAtlas", ShaderBindingKind.UniformLocation, 77, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding LumonSceneMaterialAtlasLocation { get; }
    /// <summary>Declares the vge_lumonScenePageTableMip0 UniformLocation slot.</summary>
    [ShaderBinding("vge_lumonScenePageTableMip0", ShaderBindingKind.UniformLocation, 78, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding LumonScenePageTableMip0Location { get; }
    /// <summary>Declares the vge_lumonSceneSurfaceLut UniformLocation slot.</summary>
    [ShaderBinding("vge_lumonSceneSurfaceLut", ShaderBindingKind.UniformLocation, 79, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding LumonSceneSurfaceLutLocation { get; }
    /// <summary>Declares the vge_materialAtlas UniformLocation slot.</summary>
    [ShaderBinding("vge_materialAtlas", ShaderBindingKind.UniformLocation, 80, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding MaterialAtlasLocation { get; }
    /// <summary>Declares the vge_pageTableMip0 UniformLocation slot.</summary>
    [ShaderBinding("vge_pageTableMip0", ShaderBindingKind.UniformLocation, 81, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding PageTableMip0Location { get; }
    /// <summary>Declares the vge_pageUsageStamp UniformLocation slot.</summary>
    [ShaderBinding("vge_pageUsageStamp", ShaderBindingKind.UniformLocation, 82, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding PageUsageStampLocation { get; }
    /// <summary>Declares the vge_patchIdGBuffer UniformLocation slot.</summary>
    [ShaderBinding("vge_patchIdGBuffer", ShaderBindingKind.UniformLocation, 83, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding PatchIdGLocation { get; }
    /// <summary>Declares the vge_sunLevelScalarLut UniformLocation slot.</summary>
    [ShaderBinding("vge_sunLevelScalarLut", ShaderBindingKind.UniformLocation, 84, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding SunLevelScalarLutLocation { get; }
    /// <summary>Declares the vge_surfaceLut UniformLocation slot.</summary>
    [ShaderBinding("vge_surfaceLut", ShaderBindingKind.UniformLocation, 85, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding SurfaceLutLocation { get; }
    /// <summary>Declares the worldProbeDebugState0 UniformLocation slot.</summary>
    [ShaderBinding("worldProbeDebugState0", ShaderBindingKind.UniformLocation, 86, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding WorldProbeDebugState0Location { get; }
    /// <summary>Declares the worldProbeDist0 UniformLocation slot.</summary>
    [ShaderBinding("worldProbeDist0", ShaderBindingKind.UniformLocation, 87, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding WorldProbeDist0Location { get; }
    /// <summary>Declares the worldProbeMeta0 UniformLocation slot.</summary>
    [ShaderBinding("worldProbeMeta0", ShaderBindingKind.UniformLocation, 88, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding WorldProbeMeta0Location { get; }
    /// <summary>Declares the worldProbeRadianceAtlas UniformLocation slot.</summary>
    [ShaderBinding("worldProbeRadianceAtlas", ShaderBindingKind.UniformLocation, 89, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding WorldProbeRadianceAtlasLocation { get; }
    /// <summary>Declares the worldProbeSuppressedLighting UniformLocation slot.</summary>
    [ShaderBinding("worldProbeSuppressedLighting", ShaderBindingKind.UniformLocation, 90, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding WorldProbeSuppressedLightingLocation { get; }
    /// <summary>Declares the worldProbeVis0 UniformLocation slot.</summary>
    [ShaderBinding("worldProbeVis0", ShaderBindingKind.UniformLocation, 91, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding WorldProbeVis0Location { get; }
    /// <summary>Declares the gBufferEnvironment UniformLocation slot.</summary>
    [ShaderBinding("gBufferEnvironment", ShaderBindingKind.UniformLocation, 92, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding GBufferEnvironmentLocation { get; }
    /// <summary>Declares the vge_atmosphereAerialRadiance UniformLocation slot.</summary>
    [ShaderBinding("vge_atmosphereAerialRadiance", ShaderBindingKind.UniformLocation, 93, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding AtmosphereAerialRadianceLocation { get; }
    /// <summary>Declares the vge_atmosphereAerialAttenuation UniformLocation slot.</summary>
    [ShaderBinding("vge_atmosphereAerialAttenuation", ShaderBindingKind.UniformLocation, 94, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding AtmosphereAerialAttenuationLocation { get; }
    /// <summary>Declares the gBufferPosition UniformLocation slot.</summary>
    [ShaderBinding("gBufferPosition", ShaderBindingKind.UniformLocation, 95, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderUniformLocationBinding GBufferPositionLocation { get; }
    /// <summary>Declares the uv VaryingLocation slot.</summary>
    [ShaderBinding("uv", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding UvVarying { get; }
    /// <summary>Declares the v_uv VaryingLocation slot.</summary>
    [ShaderBinding("v_uv", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding VUvVarying { get; }
    /// <summary>Declares the vTexCoord VaryingLocation slot.</summary>
    [ShaderBinding("vTexCoord", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding VTexCoordVarying { get; }
    /// <summary>Declares the vColor VaryingLocation slot.</summary>
    [ShaderBinding("vColor", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding VColorVarying { get; }
    /// <summary>Declares the vAtlasCoord VaryingLocation slot.</summary>
    [ShaderBinding("vAtlasCoord", ShaderBindingKind.VaryingLocation, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding VAtlasCoordVarying { get; }
    /// <summary>Declares the vRadiance VaryingLocation slot.</summary>
    [ShaderBinding("vRadiance", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding VRadianceVarying { get; }
    /// <summary>Declares the vAoDirWorld VaryingLocation slot.</summary>
    [ShaderBinding("vAoDirWorld", ShaderBindingKind.VaryingLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding VAoDirWorldVarying { get; }
    /// <summary>Declares the vAoConfidence VaryingLocation slot.</summary>
    [ShaderBinding("vAoConfidence", ShaderBindingKind.VaryingLocation, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding VAoConfidenceVarying { get; }
    /// <summary>Declares the vConfidence VaryingLocation slot.</summary>
    [ShaderBinding("vConfidence", ShaderBindingKind.VaryingLocation, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding VConfidenceVarying { get; }
    /// <summary>Declares the vMeanLogHitDistance VaryingLocation slot.</summary>
    [ShaderBinding("vMeanLogHitDistance", ShaderBindingKind.VaryingLocation, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding VMeanLogHitDistanceVarying { get; }
    /// <summary>Declares the vSkyIntensity VaryingLocation slot.</summary>
    [ShaderBinding("vSkyIntensity", ShaderBindingKind.VaryingLocation, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding VSkyIntensityVarying { get; }
    /// <summary>Declares the vFlags VaryingLocation slot.</summary>
    [ShaderBinding("vFlags", ShaderBindingKind.VaryingLocation, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderVaryingLocationBinding VFlagsVarying { get; }
    /// <summary>Declares the outColor FragmentOutputLocation slot.</summary>
    [ShaderBinding("outColor", ShaderBindingKind.FragmentOutputLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderFragmentOutputLocationBinding OutColorOutput { get; }
    /// <summary>Declares the fragColor FragmentOutputLocation slot.</summary>
    [ShaderBinding("fragColor", ShaderBindingKind.FragmentOutputLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderFragmentOutputLocationBinding FragColorOutput { get; }
    /// <summary>Declares the color FragmentOutputLocation slot.</summary>
    [ShaderBinding("color", ShaderBindingKind.FragmentOutputLocation, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment, ShaderStageKind.Geometry, ShaderStageKind.TessellationControl, ShaderStageKind.TessellationEvaluation, ShaderStageKind.Compute)]
    private static partial ShaderFragmentOutputLocationBinding ColorOutput { get; }
    #endregion
}
