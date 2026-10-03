#version 330 core

out vec4 outColor;
layout(location = 1) out vec4 outRefractionColor;
layout(location = 2) out float outRefractionDepth;

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
@import "./includes/liquids/boundary_transport.glsl"
layout(binding = 12) uniform sampler2D vge_waterOpticalDepth;
layout(binding = 13) uniform sampler2D vge_waterSource;

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
uniform sampler2D preOverlayColor;
uniform sampler2D preOverlayDepth;

// Fog (VS convention)

/** Integrates captured segments, requiring a known return outside water for sky backgrounds. */
bool VgeCompositeWaterTransport(vec3 receiverVS, bool sky, out vec3 transmission,
    out vec3 inScattering, out float waterLength)
{
    bool startsInWater = vgePbrCompositeParams.waterScattering.w > .5;
    vec4 source = texelFetch(vge_waterSource, ivec2(gl_FragCoord.xy), 0);
    transmission = vec3(1);
    inScattering = vec3(0);
    waterLength = 0.0;
    float terminal = source.w + (startsInWater ? 1.0 : 0.0);
    // An absent exit is unknown coverage, not an arbitrarily deep water volume.
    if (sky)
    {
        if (abs(terminal) > .01) return false;
    }
    source.rgb += vgePbrCompositeParams.waterCameraSource.rgb * length(receiverVS);
    return VgeWaterBoundaryTransport(texelFetch(vge_waterOpticalDepth, ivec2(gl_FragCoord.xy), 0),
        source, vgePbrCompositeParams.waterAbsorption.rgb + vgePbrCompositeParams.waterScattering.rgb,
        startsInWater, length(receiverVS), transmission, inScattering, waterLength);
}

void main(void)
{
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(primaryDepth, 0));

    float depth = texture(primaryDepth, uv).r;
    // Sky and first-person visibility proxies cannot establish a refracted hit.
    if (vgePbrCompositeParams.fogFloats0.w > .5)
    {
        outRefractionDepth = depth;
        outRefractionColor = vec4(0);
    }

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
        bool waterCaptureEnabled = vgePbrCompositeParams.waterAbsorption.w > .5;
        bool cameraAboveWater = vgePbrCompositeParams.fogFloats0.z < .5;
        bool startsInWater = vgePbrCompositeParams.waterScattering.w > .5;
        bool cameraMediumSupported = cameraAboveWater || startsInWater;
        if (waterCaptureEnabled && cameraMediumSupported)
        {
            vec3 transmission;
            vec3 inScattering;
            float waterLength;
            bool resolved = VgeCompositeWaterTransport(receiverVS, true, transmission, inScattering, waterLength);
            if (resolved) skyColor = skyColor * transmission + inScattering;
        }
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
        // In Vintage Story content, gBufferMaterial.a is transmission (not AO), so using it
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

    // Capture before water/atmospheric transport; the liquid evaluates its bent path once.
    if (vgePbrCompositeParams.fogFloats0.w > .5)
    {
        outRefractionColor = vec4(finalColor, texture(gBufferNormal, uv).a >= 0.0 ? 1.0 : 0.0);
        // Restore both members of the clean pair only where first-person visibility replaced the world.
        // Optional missing captures bind the zero fallback and never establish a physical receiver.
        if (texture(gBufferNormal, uv).a < 0.0 && vgePbrCompositeParams.aoStrengths.z > .5)
        {
            outRefractionColor = texelFetch(preOverlayColor, ivec2(gl_FragCoord.xy), 0);
            outRefractionDepth = texelFetch(preOverlayDepth, ivec2(gl_FragCoord.xy), 0).r;
        }
    }

    bool waterResolved = false;
    bool waterCaptureEnabled = vgePbrCompositeParams.waterAbsorption.w > .5;
    bool hasPhysicalReceiver = texture(gBufferNormal, uv).a >= 0.0;
    bool cameraAboveWater = vgePbrCompositeParams.fogFloats0.z < .5;
    bool startsInWater = vgePbrCompositeParams.waterScattering.w > .5;
    bool cameraMediumSupported = cameraAboveWater || startsInWater;
    if (waterCaptureEnabled && hasPhysicalReceiver && cameraMediumSupported)
    {
        vec3 transmission;
        vec3 inScattering;
        float waterLength;
        waterResolved = VgeCompositeWaterTransport(receiverVS, false, transmission, inScattering, waterLength);
        if (waterResolved)
        {
            // Apply aerial perspective only to the aggregate air portion of the ray.
            vec3 airReceiver = transpose(mat3(viewMatrix)) * receiverVS
                * max(0.0, 1.0 - waterLength / max(length(receiverVS), .001));
            finalColor = VgeApplyAerial(finalColor, airReceiver, texture(gBufferEnvironment, uv).a,
                vgePbrCompositeParams.atmosphereAerial.xy, vgePbrCompositeParams.atmosphereSun.xyz);
            finalColor = finalColor * transmission + inScattering;
        }
    }
    if (!waterResolved && vgePbrCompositeParams.fogFloats0.z > .5)
    {
        float fogAmount = clamp(fogMinIn + 1.0 - exp(-length(receiverVS) * fogDensityIn), 0.0, 1.0);
        finalColor = mix(finalColor, VgeSrgbToLinear(rgbaFogIn.rgb), fogAmount);
    }
    else if (!waterResolved)
        finalColor = VgeApplyAerial(finalColor, transpose(mat3(viewMatrix)) * receiverVS,
            texture(gBufferEnvironment, uv).a, vgePbrCompositeParams.atmosphereAerial.xy, vgePbrCompositeParams.atmosphereSun.xyz);

    outColor = vec4(finalColor, 1.0);
}
