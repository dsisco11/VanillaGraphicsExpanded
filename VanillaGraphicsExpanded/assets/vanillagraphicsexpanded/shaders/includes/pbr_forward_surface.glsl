#ifndef VGE_PBR_FORWARD_SURFACE_GLSL
#define VGE_PBR_FORWARD_SURFACE_GLSL
@import "./pbr_environment.glsl"
#ifndef VGE_PBR_FORWARD_LUMON
#error Forward PBR requires an explicit lighting mode before compilation.
#endif


#if DYNLIGHTS > 0
uniform vec3 pointLights[DYNLIGHTS];
uniform vec3 pointLightColors[DYNLIGHTS];
uniform int pointLightQuantity;
#endif

/** Samples engine-provided cascade coordinates without the vanilla ambient brightness floor. */
float VgeForwardOcclusion(sampler2DShadow map, vec4 coords, float bias)
{
    if (coords.w <= 0.0) return 0.0;
    vec2 texel = 1.0 / vec2(max(textureSize(map, 0), ivec2(1)));
    float lit = 0.0;
    for (int x = -1; x <= 1; ++x)
        for (int y = -1; y <= 1; ++y)
            lit += texture(map, vec3(coords.xy + vec2(x, y) * texel, coords.z - bias));
    return (1.0 - lit / 9.0) * coords.w;
}

/** Evaluates late/OIT radiance before the engine blends its color; alpha remains owned by the caller. */
vec3 VgeForwardSurface(vec3 baseColor, vec3 N, vec3 material, float fog, float transmission)
{
    mat3 toWorld = transpose(mat3(VGE_SURFACE_VIEW));
    vec3 V = normalize(toWorld * -vge_viewPosition);
    float occlusion = 0.0;
    #if SHADOWQUALITY > 0
    occlusion += VgeForwardOcclusion(shadowMapFar, shadowCoordsFar, 0.0009);
    #endif
    #if SHADOWQUALITY > 1
    occlusion += VgeForwardOcclusion(shadowMapNear, shadowCoordsNear, 0.0005);
    #endif
    float visibility = 1.0 - clamp(occlusion * shadowIntensity, 0.0, 1.0);
    float roughness = clamp(material.r, 0.04, 1.0);
    float metallic = clamp(material.g, 0.0, 1.0);
    vec3 diffuse = vec3(0.0), specular = vec3(0.0);
    addDirectLight(baseColor, N, V, normalize(vge_atmosphereSunDirection), (vge_atmosphereSolar * vge_skyVisibility) * visibility,
        roughness, metallic, diffuse, specular);
    // Normalize only physical sunlight; existing engine point-light units retain their calibration.
    diffuse /= 3.14159265359;
    diffuse += VgeTransmission(baseColor, N, V, normalize(vge_atmosphereSunDirection),
        vge_atmosphereSolar * vge_skyVisibility, metallic, transmission, clamp(1.0 - occlusion, 0.0, 1.0));
    #if DYNLIGHTS > 0
    for (int i = 0; i < min(pointLightQuantity, DYNLIGHTS); ++i)
    {
        vec3 delta = pointLights[i] - vge_viewPosition;
        float distanceSquared = max(dot(delta, delta), 0.0001);
        vec3 direction = toWorld * delta;
        direction *= inversesqrt(max(dot(direction, direction), 0.0001));
        addDirectLight(baseColor, N, V, direction, pointLightColors[i] * min(1.0 / distanceSquared, 1.0),
            roughness, metallic, diffuse, specular);
    }
    #endif
    // Local block illumination is a bounded fallback for these receivers, not screen-space GI
    // sampled from an unrelated opaque surface behind a transparent/first-person mesh.
    #if VGE_PBR_FORWARD_LUMON
    vec3 localDiffuse = baseColor * vge_blockIrradiance * (1.0 - metallic);
    #else
    vec3 localDiffuse = VgeEnvironmentResponse(VgeLocalEnvironment(vge_blockIrradiance, vge_sunIrradiance),
        baseColor, metallic, roughness, dot(N, V));
    #endif
    vec3 radiance = diffuse + specular + localDiffuse + baseColor * max(material.b, 0.0);
    // Atmosphere owns air transport. Retain the engine's separate underwater medium,
    // but do not blend its distance haze over the atmospheric result a second time.
    if (vge_atmosphereAerialParams.z > .7)
        radiance = mix(radiance, VgeSrgbToLinear(rgbaFog.rgb), clamp(fog, 0.0, 1.0));
    else
        radiance = VgeApplyAerial(radiance, toWorld * vge_viewPosition, vge_skyVisibility, vge_atmosphereAerialParams.xy, vge_atmosphereSunDirection);
    return VgeSceneOutput(radiance, gl_FragCoord.xy, vge_pbrRoute != 0);
}
/** Preserves non-transmitting callers which have no material transmission metadata. */
vec3 VgeForwardSurface(vec3 baseColor, vec3 N, vec3 material, float fog)
{
    return VgeForwardSurface(baseColor, N, material, fog, 0.0);
}
#endif
