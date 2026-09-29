#version 330 core

out vec4 outColor;

// ============================================================================
// PBR Composite Pass
//
// Merges direct radiance buffers (diffuse/specular/emissive) with optional
// indirect diffuse (LumOn) and applies fog once.
// Output remains scene-linear in RGBA16F until the separate display resolve.
// ============================================================================

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_pbr.glsl"
@import "./includes/pbr_color.glsl"
@import "./includes/pbr_environment.glsl"
@import "./includes/atmosphere_aerial.glsl"

// Import global defines (feature toggles with defaults)
@import "./includes/vge_global_defines.glsl"

@import "./includes/pbr_composite_params_ubo.glsl"

// Direct buffers (linear, fog-free)
uniform sampler2D directDiffuse;
uniform sampler2D directSpecular;
uniform sampler2D emissive;

// Optional indirect (linear, fog-free)
#if VGE_LUMON_ENABLED
uniform sampler2D indirectDiffuse;
#endif
uniform sampler2D gBufferEnvironment;

// G-Buffer
uniform sampler2D gBufferAlbedo;
uniform sampler2D gBufferMaterial;
uniform sampler2D gBufferNormal;
uniform sampler2D primaryDepth;
uniform sampler2D gBufferPosition;

// Fog (VS convention)

void main(void)
{
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(primaryDepth, 0));

    float depth = texture(primaryDepth, uv).r;

    vec3 directLight = texture(directDiffuse, uv).rgb + texture(directSpecular, uv).rgb;
    vec3 emissiveLight = texture(emissive, uv).rgb;
    vec3 finalColor = directLight + emissiveLight;

    // First-person visibility depth cannot reconstruct a physical lighting or fog receiver.
    vec3 receiverVS = texture(gBufferNormal, uv).a < 0.0
        ? texelFetch(gBufferPosition, ivec2(gl_FragCoord.xy), 0).xyz
        : lumonReconstructViewPos(uv, depth, invProjectionMatrix);

    // Sky: skip indirect + fog
    if (lumonIsSky(depth))
    {
        // Direct lighting pass outputs 0 for sky/background by design.
        // Preserve the base scene color here (sky shader output lives in gBufferAlbedo).
        vec3 skyColor = texture(gBufferAlbedo, uv).rgb;
        outColor = vec4(max(skyColor, vec3(0.0)), 1.0);
        return;
    }

#if VGE_LUMON_ENABLED
    vec3 indirect = indirectIntensity > 0.0 ? texture(indirectDiffuse, uv).rgb : vec3(0.0);

        vec3 albedo = lumonGetAlbedo(gBufferAlbedo, uv);
        float roughness;
        float metallic;
        float emissive;
        float reflectivity;
        lumonGetMaterialProperties(gBufferMaterial, uv, roughness, metallic, emissive, reflectivity);

        indirect *= indirectIntensity;
        indirect *= indirectTint;

#if !VGE_LUMON_PBR_COMPOSITE
        vec3 combined = lumonCombineLighting(directLight, indirect, albedo, metallic, 1.0, vec3(1.0));
        finalColor = combined + emissiveLight;
#else
        vec3 viewPosVS = receiverVS;
        vec3 viewDirVS = normalize(-viewPosVS);

        vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);
        vec3 normalVS = normalize((viewMatrix * vec4(normalWS, 0.0)).xyz);

        // AO is intentionally a no-op for now.
        // In Vintage Story content, gBufferMaterial.a is reflectivity (not AO), so using it
        // as an occlusion term can incorrectly attenuate/wipe indirect lighting.
        // TODO: When LumOn provides a dedicated short-range AO signal, wire it here.
        float ao = 1.0;

        vec3 shortRangeAoDirVS = normalVS;
    #if VGE_LUMON_ENABLE_SHORT_RANGE_AO
        float bend = clamp((1.0 - clamp(ao, 0.0, 1.0)) * 0.5, 0.0, 0.5);
        shortRangeAoDirVS = normalize(mix(normalVS, vec3(0.0, 1.0, 0.0), bend));
#endif

            vec3 indirectDiffuseContrib;
            vec3 indirectSpecularContrib;

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
                indirectDiffuseContrib,
                indirectSpecularContrib);

            finalColor = directLight + emissiveLight + indirectDiffuseContrib + indirectSpecularContrib;
#endif // VGE_LUMON_PBR_COMPOSITE
#else
    vec3 albedo = texture(gBufferAlbedo, uv).rgb;
    vec4 material = texture(gBufferMaterial, uv);
    vec3 normalVS = normalize(mat3(viewMatrix) * lumonDecodeNormal(texture(gBufferNormal, uv).xyz));
    vec3 toEye = normalize(-receiverVS);
    finalColor += VgeEnvironmentResponse(texture(gBufferEnvironment, uv).rgb,
        albedo, material.g, material.r, dot(normalVS, toEye));
#endif // VGE_LUMON_ENABLED

    finalColor = max(finalColor, vec3(0.0));

    if (vgePbrCompositeParams.fogFloats0.z > .5)
    {
        float fogAmount = clamp(fogMinIn + 1.0 - exp(-length(receiverVS) * fogDensityIn), 0.0, 1.0);
        finalColor = mix(finalColor, VgeSrgbToLinear(rgbaFogIn.rgb), fogAmount);
    }
    else
        finalColor = VgeApplyAerial(finalColor, transpose(mat3(viewMatrix)) * receiverVS,
            texture(gBufferEnvironment, uv).a, vgePbrCompositeParams.atmosphereAerial.xy, vgePbrCompositeParams.atmosphereSun.xyz);

    outColor = vec4(finalColor, 1.0);
}
