#version 330 core

in vec2 uv;

layout(location = 0) out vec4 outDirectDiffuse;
layout(location = 1) out vec4 outDirectSpecular;
layout(location = 2) out vec4 outEmissive;

// Scene inputs
uniform sampler2D primaryScene;   // ColorAttachment0: baseColor (linear)
uniform sampler2D primaryDepth;

// VGE G-buffer inputs
uniform sampler2D gBufferNormal;   // ColorAttachment4: normal packed (RGBA16F)
uniform sampler2D gBufferMaterial; // ColorAttachment5: Roughness, Metallic, Emissive, Reflectivity (RGBA16F)

@import "./includes/pbr_direct_lighting_params_ubo.glsl"

// Shadows (wired in Phase 16.3/16.4; shader defines the surface now)
uniform sampler2DShadow shadowMapNear;
uniform sampler2DShadow shadowMapFar;

@import "./includes/pbr_common.glsl"
@import "./includes/pbr_shadowmaps.glsl"

const float PI = 3.141592653589793;


float linearizeDepth(float depth)
{
    float z = depth * 2.0 - 1.0;
    return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
}

vec3 reconstructViewPos(vec2 texCoord, float depth)
{
    vec4 ndc = vec4(texCoord * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    vec4 viewPos = invProjectionMatrix * ndc;
    viewPos /= viewPos.w;
    return viewPos.xyz;
}

void addDirectLight(
    vec3 baseColor,
    vec3 N,
    vec3 V,
    vec3 L,
    vec3 lightRgb,
    float roughness,
    float metallic,
    float reflectivity,
    inout vec3 accumDiffuse,
    inout vec3 accumSpecular)
{
    float NdotL = max(dot(N, L), 0.0);
    if (NdotL <= 0.0) return;

    vec3 dielectricF0 = vec3(0.04) * clamp(reflectivity, 0.0, 1.0);
    vec3 F0 = mix(dielectricF0, baseColor, clamp(metallic, 0.0, 1.0));

    vec3 H = normalize(V + L);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);

    vec3 kD = pbrDiffuseFactorFromFresnel(F, metallic);
    vec3 kS = pbrSpecularFactorFromFresnel(F);

    // NOTE: VS's lighting inputs here are not calibrated as physical radiance.
    // Using the normalized Lambert term (albedo / PI) makes this pass look far too dark
    // compared to the game's legacy lighting model. Treat the inputs as already-integrated
    // irradiance and do not apply 1/PI.
    vec3 diffuseBrdf = kD * baseColor;
    vec3 specularBrdf = cookTorranceBRDF(N, V, L, F0, roughness);

    // Radiance split
    accumDiffuse += diffuseBrdf * lightRgb * NdotL;
    accumSpecular += (kS * specularBrdf) * lightRgb * NdotL;
}

void main()
{
    vec4 baseColorTex = texture(primaryScene, uv);
    float depth = texture(primaryDepth, uv).r;

    // Sky / background: no direct lighting contribution
    if (depth >= 0.999999)
    {
        outDirectDiffuse = vec4(0.0);
        outDirectSpecular = vec4(0.0);
        outEmissive = vec4(0.0);
        return;
    }

    vec3 viewPos = reconstructViewPos(uv, depth);

    // Shadow lookup uses the terrain position reconstructed through the full inverse view.
    vec3 worldPosRel = (invModelViewMatrix * vec4(viewPos, 1.0)).xyz;

    vec4 nPacked = texture(gBufferNormal, uv);
    vec3 N = normalize(nPacked.rgb * 2.0 - 1.0);

    vec4 m = texture(gBufferMaterial, uv);
    // Minimum roughness clamp: avoids GGX singularities that can overflow RGBA16F outputs.
    float roughness = clamp(m.r, 0.04, 1.0);
    float metallic = clamp(m.g, 0.0, 1.0);
    float emissiveScalar = max(m.b, 0.0);
    float reflectivity = clamp(m.a, 0.0, 1.0);

    vec3 baseColor = baseColorTex.rgb;

    // View vector in world space
    vec3 viewDirView = normalize(-viewPos);
    vec3 V = normalize((invModelViewMatrix * vec4(viewDirView, 0.0)).xyz);

    vec3 accumDiffuse = vec3(0.0);
    vec3 accumSpecular = vec3(0.0);

    // Directional (sun)
    vec3 Lsun = normalize(lightDirection);
    float sunVis = pbrComputeSunShadowVisibility(worldPosRel);
    addDirectLight(
        baseColor,
        N,
        V,
        Lsun,
        rgbaLightIn * sunVis,
        roughness,
        metallic,
        reflectivity,
        accumDiffuse,
        accumSpecular);

    // Vanilla passes camPos into applyLight: its point-light array is in view space.
    // Measure distance there, then rotate the direction into the world space of N and V.
    int count = clamp(pointLightsCount, 0, 100);
    for (int i = 0; i < count; i++)
    {
        vec3 lp = VgePbrPointLightPos(i);
        vec3 lc = VgePbrPointLightColor(i);

        vec3 toLightVS = lp - viewPos;
        float distSq = max(dot(toLightVS, toLightVS), 0.0001);
        vec3 toLightWS = (invModelViewMatrix * vec4(toLightVS, 0.0)).xyz;
        vec3 L = toLightWS * inversesqrt(max(dot(toLightWS, toLightWS), 0.0001));

        // Simple inverse-square attenuation (clamped)
        float att = min(1.0 / distSq, 1.0);

        addDirectLight(
            baseColor,
            N,
            V,
            L,
            lc * att,
            roughness,
            metallic,
            reflectivity,
            accumDiffuse,
            accumSpecular);
    }

    // No fog in this pass (fog applied in final composite)

    // Emissive stored separately
    vec3 emissive = baseColor * emissiveScalar;

    outDirectDiffuse = vec4(accumDiffuse, 1.0);
    outDirectSpecular = vec4(accumSpecular, 1.0);
    outEmissive = vec4(emissive, 1.0);
}
