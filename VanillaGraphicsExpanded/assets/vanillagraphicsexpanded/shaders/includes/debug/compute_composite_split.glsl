#ifndef LUMON_DEBUG_COMPUTE_COMPOSITE_SPLIT_GLSL
#define LUMON_DEBUG_COMPUTE_COMPOSITE_SPLIT_GLSL
@import "./get_view_matrix.glsl"

/** Implements compute composite split for its explicit view entrypoint. */
void computeCompositeSplit(
    out float outAo,
    out float outRoughness,
    out float outMetallic,
    out vec3 outIndirectDiffuse,
    out vec3 outIndirectSpecular)
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        outAo = 1.0;
        outRoughness = 0.0;
        outMetallic = 0.0;
        outIndirectDiffuse = vec3(0.0);
        outIndirectSpecular = vec3(0.0);
        return;
    }

    vec3 indirect = texture(indirectDiffuseFull, uv).rgb;
    indirect *= indirectIntensity;
    indirect *= indirectTint;

    vec3 albedo = lumonGetAlbedo(gBufferAlbedo, uv);

    float roughness;
    float metallic;
    float emissive;
    float reflectivity;
    lumonGetMaterialProperties(gBufferSurface, uv, roughness, metallic, emissive, reflectivity);

    // AO is not implemented yet. Keep it as a no-op (1.0).
    // NOTE: gBufferMaterial.a is reflectivity, not AO.
    float ao = 1.0;
#if VGE_LUMON_ENABLE_AO
    // NaN-guard references: does not change behavior for valid values.
    if (diffuseAOStrength != diffuseAOStrength) ao = 0.0;
    if (specularAOStrength != specularAOStrength) ao = 0.0;
#endif

    // Legacy path compatibility: if PBR composite is off, treat all indirect as diffuse.
#if !VGE_LUMON_PBR_COMPOSITE
    outAo = ao;
    outRoughness = roughness;
    outMetallic = metallic;
    outIndirectDiffuse = indirect;
    outIndirectSpecular = vec3(0.0);
    return;
#else

    vec3 viewPosVS = lumonReconstructViewPos(uv, depth, vgeFrame.invProjectionMatrix);
    vec3 viewDirVS = normalize(-viewPosVS);

    vec3 normalWS = lumonDecodeNormal(texture(gBufferSurface, vec3(uv, VGE_SURFACE_NORMAL)).xyz);
    vec3 normalVS = normalize((getViewMatrix() * vec4(normalWS, 0.0)).xyz);

    vec3 shortRangeAoDirVS = normalVS;
#if VGE_LUMON_ENABLE_SHORT_RANGE_AO
    float bend = clamp((1.0 - clamp(ao, 0.0, 1.0)) * 0.5, 0.0, 0.5);
    shortRangeAoDirVS = normalize(mix(normalVS, vec3(0.0, 1.0, 0.0), bend));
#endif

    vec3 diffuseContrib;
    vec3 specContrib;

    lumonComputeIndirectSplit(
        indirect,
        albedo,
        shortRangeAoDirVS,
        viewDirVS,
        roughness,
        metallic,
        ao,
        diffuseAOStrength,
        specularAOStrength,
        diffuseContrib,
        specContrib);

    outAo = ao;
    outRoughness = roughness;
    outMetallic = metallic;
    outIndirectDiffuse = diffuseContrib;
    outIndirectSpecular = specContrib;
#endif // VGE_LUMON_PBR_COMPOSITE
}
#endif
