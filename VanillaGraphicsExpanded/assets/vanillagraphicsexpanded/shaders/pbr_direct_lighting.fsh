#version 450 core
@import "./includes/pbr_scene_storage.glsl"
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

@import "./includes/pbr_direct_lighting_inputs.glsl"

@import "./includes/pbr_common.glsl"

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
@import "./includes/pbr_celestial_direct.glsl"

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

    // Both celestial sources use the current illumination snapshot; their shadows may
    // use independently retained rendered mappings selected by the shared readiness gate.
    float skyVisibility = texture(gBufferSurface, vec3(uv, VGE_SURFACE_ENVIRONMENT)).a;
    vec3 transmission = vec3(0.0);
    VgeAccumulateCelestialDirect(0, vgeSurfaceSunDirection, vgeSurfaceSolar, vgeSurfaceSunRadius,
        worldPosRel, baseColor, N, V, skyVisibility, roughness, metallic, m.a,
        accumDiffuse, accumSpecular, transmission);
    VgeAccumulateCelestialDirect(1, vgeSurfaceMoonDirection, vgeSurfaceLunar, vgeSurfaceMoonRadius,
        worldPosRel, baseColor, N, V, skyVisibility, roughness, metallic, m.a,
        accumDiffuse, accumSpecular, transmission);

    // Vanilla passes camPos into applyLight: its point-light array is in view space.
    // Measure distance there, then rotate the direction into the world space of N and V.
    int count = clamp(pointLightsCount, 0, vgeLights.positions.length());
    for (int i = 0; i < count; i++)
    {
        vec3 lp = vgeLights.positions[i].xyz;

        vec3 toLightVS = lp - viewPos;
        float distSq = max(dot(toLightVS, toLightVS), 0.0001);
        vec3 toLightWS = (invModelViewMatrix * vec4(toLightVS, 0.0)).xyz;
        vec3 L = toLightWS * inversesqrt(max(dot(toLightWS, toLightWS), 0.0001));

        addDirectLight(
            baseColor,
            N,
            V,
            L,
            VgePointIlluminance(vgeLights.colors[i], distSq)
                * vgeSurfaceShadowVisibility(2 + i, worldPosRel, N),
            roughness,
            metallic,
            accumDiffuse,
            accumSpecular);
    }

    // Thin transmission spends the same non-specular budget as front diffuse reflection.
    accumDiffuse = accumDiffuse * (1.0-clamp(m.a,0.0,1.0)) + transmission;
    // No fog in this pass (fog applied in final composite)

    // Emissive stored separately
    vec3 emissive = VgeEmissionLuminance(baseColor, emissiveScalar);

    // Preserve isolated transmission RGB in the otherwise unused attachment alpha channels.
    vec3 storedTransmission = VgeStoreSceneLighting(transmission);
    outDirectDiffuse = vec4(VgeStoreSceneLighting(accumDiffuse), storedTransmission.r);
    outDirectSpecular = vec4(VgeStoreSceneLighting(accumSpecular), storedTransmission.g);
    outEmissive = vec4(VgeStoreSceneLighting(emissive), storedTransmission.b);
}
