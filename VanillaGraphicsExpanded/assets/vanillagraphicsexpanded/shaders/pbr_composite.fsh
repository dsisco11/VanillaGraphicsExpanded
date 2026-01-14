#version 330 core

out vec4 outColor;

// ============================================================================
// PBR Composite Pass
//
// Merges direct radiance buffers (diffuse/specular/emissive) with optional
// indirect diffuse (LumOn) and applies fog once.
// Output is written to the primary framebuffer ColorAttachment0.
// ============================================================================

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_pbr.glsl"

// Direct buffers (linear, fog-free)
uniform sampler2D directDiffuse;
uniform sampler2D directSpecular;
uniform sampler2D emissive;

// Optional indirect (linear, fog-free)
uniform sampler2D indirectDiffuse;

// G-Buffer
uniform sampler2D gBufferAlbedo;
uniform sampler2D gBufferMaterial;
uniform sampler2D gBufferNormal;
uniform sampler2D primaryDepth;

// Fog (VS convention)
uniform vec4 rgbaFogIn;
uniform float fogDensityIn;
uniform float fogMinIn;

// Indirect controls
uniform float indirectIntensity;
uniform vec3 indirectTint;
uniform int lumOnEnabled;

// Phase 15 toggles
uniform int enablePbrComposite;
uniform int enableAO;
uniform int enableBentNormal;
uniform float diffuseAOStrength;
uniform float specularAOStrength;

uniform mat4 invProjectionMatrix;
uniform mat4 viewMatrix;

void main(void)
{
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(primaryDepth, 0));

    float depth = texture(primaryDepth, uv).r;

    vec3 directLight = texture(directDiffuse, uv).rgb + texture(directSpecular, uv).rgb;
    vec3 emissiveLight = texture(emissive, uv).rgb;
    vec3 finalColor = directLight + emissiveLight;

    // Sky: skip indirect + fog
    if (lumonIsSky(depth))
    {
        // Direct lighting pass outputs 0 for sky/background by design.
        // Preserve the base scene color here (sky shader output lives in gBufferAlbedo).
        vec3 skyColor = texture(gBufferAlbedo, uv).rgb;
        outColor = vec4(max(skyColor, vec3(0.0)), 1.0);
        return;
    }

    if (lumOnEnabled == 1)
    {
        vec3 indirect = texture(indirectDiffuse, uv).rgb;

        vec3 albedo = lumonGetAlbedo(gBufferAlbedo, uv);
        float roughness;
        float metallic;
        float emissive;
        float reflectivity;
        lumonGetMaterialProperties(gBufferMaterial, uv, roughness, metallic, emissive, reflectivity);

        indirect *= indirectIntensity;
        indirect *= indirectTint;

        if (enablePbrComposite == 0)
        {
            vec3 combined = lumonCombineLighting(directLight, indirect, albedo, metallic, 1.0, vec3(1.0));
            finalColor = combined + emissiveLight;
        }
        else
        {
            vec3 viewPosVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
            vec3 viewDirVS = normalize(-viewPosVS);

            vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);
            vec3 normalVS = normalize((viewMatrix * vec4(normalWS, 0.0)).xyz);

            // AO is intentionally a no-op for now.
            // In Vintage Story content, gBufferMaterial.a is reflectivity (not AO), so using it
            // as an occlusion term can incorrectly attenuate/wipe indirect lighting.
            // TODO: When LumOn provides a dedicated short-range AO signal, wire it here.
            float ao = 1.0;

            vec3 bentNormalVS = normalVS;
            if (enableBentNormal == 1)
            {
                float bend = clamp((1.0 - clamp(ao, 0.0, 1.0)) * 0.5, 0.0, 0.5);
                bentNormalVS = normalize(mix(normalVS, vec3(0.0, 1.0, 0.0), bend));
            }

            vec3 indirectDiffuseContrib;
            vec3 indirectSpecularContrib;

            lumonComputeIndirectSplit(
                indirect,
                albedo,
                bentNormalVS,
                viewDirVS,
                roughness,
                metallic,
                ao,
                diffuseAOStrength,
                specularAOStrength,
                indirectDiffuseContrib,
                indirectSpecularContrib);

            finalColor = directLight + emissiveLight + indirectDiffuseContrib + indirectSpecularContrib;
        }
    }

    finalColor = max(finalColor, vec3(0.0));

    float fogAmount = clamp(fogMinIn + 1.0 - 1.0 / exp(depth * fogDensityIn), 0.0, 1.0);
    finalColor = mix(finalColor, rgbaFogIn.rgb, fogAmount);

    outColor = vec4(finalColor, 1.0);
}
