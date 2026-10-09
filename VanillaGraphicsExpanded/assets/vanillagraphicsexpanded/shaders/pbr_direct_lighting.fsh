#version 330 core
@import "./includes/gbuffer_layers.glsl"

in vec2 uv;

layout(location = 0) out vec4 outDirectDiffuse;
layout(location = 1) out vec4 outDirectSpecular;
layout(location = 2) out vec4 outEmissive;

// Scene inputs
uniform sampler2D primaryScene;   // ColorAttachment0: baseColor (linear)
uniform sampler2D primaryDepth;
uniform sampler2D gBufferPosition; // Unbiased first-person view position, selected by negative normal alpha.

// VGE G-buffer inputs
uniform sampler2DArray gBufferSurface;

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

@import "./includes/pbr_direct_brdf.glsl"

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

    vec4 nPacked = texture(gBufferSurface, vec3(uv, VGE_SURFACE_NORMAL));
    vec3 viewPos = nPacked.a < 0.0
        ? texelFetch(gBufferPosition, ivec2(gl_FragCoord.xy), 0).xyz
        : reconstructViewPos(uv, depth);

    // Shadow lookup uses the terrain position reconstructed through the full inverse view.
    vec3 worldPosRel = (invModelViewMatrix * vec4(viewPos, 1.0)).xyz;

    vec3 N = normalize(nPacked.rgb * 2.0 - 1.0);

    vec4 m = texture(gBufferSurface, vec3(uv, VGE_SURFACE_MATERIAL));
    // Minimum roughness clamp: avoids GGX singularities that can overflow RGBA16F outputs.
    float roughness = clamp(m.r, 0.04, 1.0);
    float metallic = clamp(m.g, 0.0, 1.0);
    float emissiveScalar = max(m.b, 0.0);

    vec3 baseColor = baseColorTex.rgb;

    // View vector in world space
    vec3 viewDirView = normalize(-viewPos);
    vec3 V = normalize((invModelViewMatrix * vec4(viewDirView, 0.0)).xyz);

    vec3 accumDiffuse = vec3(0.0);
    vec3 accumSpecular = vec3(0.0);

    // Directional (sun)
    vec3 Lsun = normalize(lightDirection);
    float sunVis;
    float sunPcfVis;
    pbrComputeSunShadowVisibility(worldPosRel, sunVis, sunPcfVis);
    // Match forward lighting: propagated sunlight supplements geometric shadow visibility.
    float skyVisibility = texture(gBufferSurface, vec3(uv, VGE_SURFACE_ENVIRONMENT)).a;
    addDirectLight(
        baseColor,
        N,
        V,
        Lsun,
        rgbaLightIn * skyVisibility,
        roughness,
        metallic,
        accumDiffuse,
        accumSpecular);

    accumDiffuse *= sunVis;
    accumSpecular *= sunPcfVis;

    // Atmosphere supplies irradiance rather than the legacy pre-scaled lighting convention.
    accumDiffuse /= 3.14159265359;
    vec3 transmission = VgeTransmission(baseColor, N, V, Lsun, rgbaLightIn * skyVisibility,
        metallic, m.a, sunPcfVis);
    accumDiffuse += transmission;

    // Vanilla passes camPos into applyLight: its point-light array is in view space.
    // Measure distance there, then rotate the direction into the world space of N and V.
    int count = clamp(pointLightsCount, 0, 100);
    for (int i = 0; i < count; i++)
    {
        vec3 lp = vgeLights.positions[i].xyz;
        vec3 lc = vgeLights.colors[i].xyz;

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
            accumDiffuse,
            accumSpecular);
    }

    // No fog in this pass (fog applied in final composite)

    // Emissive stored separately
    vec3 emissive = baseColor * emissiveScalar;

    // Preserve isolated transmission RGB in the otherwise unused attachment alpha channels.
    outDirectDiffuse = vec4(accumDiffuse, transmission.r);
    outDirectSpecular = vec4(accumSpecular, transmission.g);
    outEmissive = vec4(emissive, transmission.b);
}
