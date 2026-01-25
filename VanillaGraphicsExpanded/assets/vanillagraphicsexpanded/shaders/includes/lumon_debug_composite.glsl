// Debug Mode 18-21: Composite Debug Views (Phase 15)

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
    lumonGetMaterialProperties(gBufferMaterial, uv, roughness, metallic, emissive, reflectivity);

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

    vec3 viewPosVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 viewDirVS = normalize(-viewPosVS);

    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);
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

vec4 renderCompositeAoDebug()
{
    float ao;
    float roughness;
    float metallic;
    vec3 diff;
    vec3 spec;
    computeCompositeSplit(ao, roughness, metallic, diff, spec);
    return vec4(vec3(clamp(ao, 0.0, 1.0)), 1.0);
}

vec4 renderCompositeIndirectDiffuseDebug()
{
    float ao;
    float roughness;
    float metallic;
    vec3 diff;
    vec3 spec;
    computeCompositeSplit(ao, roughness, metallic, diff, spec);
    return vec4(diff, 1.0);
}

vec4 renderCompositeIndirectSpecularDebug()
{
    float ao;
    float roughness;
    float metallic;
    vec3 diff;
    vec3 spec;
    computeCompositeSplit(ao, roughness, metallic, diff, spec);
    return vec4(spec, 1.0);
}

vec4 renderCompositeMaterialDebug()
{
    float ao;
    float roughness;
    float metallic;
    vec3 diff;
    vec3 spec;
    computeCompositeSplit(ao, roughness, metallic, diff, spec);
    return vec4(clamp(vec3(metallic, roughness, ao), 0.0, 1.0), 1.0);
}

// Program entry: Composite
vec4 RenderDebug_Composite(vec2 screenPos)
{
    switch (debugMode)
    {
        case 18: return renderCompositeAoDebug();
        case 19: return renderCompositeIndirectDiffuseDebug();
        case 20: return renderCompositeIndirectSpecularDebug();
        case 21: return renderCompositeMaterialDebug();
        default: return vec4(0.0, 0.0, 0.0, 1.0);
    }
}
