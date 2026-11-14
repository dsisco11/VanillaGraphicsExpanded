#ifndef VGE_PBR_FORWARD_SURFACE_GLSL
#define VGE_PBR_FORWARD_SURFACE_GLSL
@import "./pbr_scene_storage.glsl"
@import "./pbr_environment.glsl"
#ifndef VGE_PBR_FORWARD_LUMON
#error Forward PBR requires an explicit lighting mode before compilation.
#endif


@import "./vge_lights_ubo.glsl"
@import "./shadows/surface.glsl"
@import "./pbr_celestial_direct.glsl"

/** Evaluates late/OIT radiance before the engine blends its color; alpha remains owned by the caller. */
vec3 VgeForwardSurface(vec3 baseColor, vec3 N, vec3 material, float fog, float transmission)
{
    mat3 toWorld = mat3(vgeFrame.invViewMatrix);
    vec3 V = normalize(toWorld * -vge_viewPosition);
    vec3 receiver = (vgeFrame.invViewMatrix * vec4(vge_viewPosition, 1.0)).xyz;
    float roughness = clamp(material.r, 0.04, 1.0);
    float metallic = clamp(material.g, 0.0, 1.0);
    vec3 diffuse = vec3(0.0), specular = vec3(0.0);
    vec3 transmitted = vec3(0);
    VgeAccumulateCelestialDirect(0, vgeSurfaceSunDirection, vgeSurfaceSolar, vgeSurfaceSunRadius,
        receiver, baseColor, N, V, vge_skyVisibility, roughness, metallic, transmission,
        diffuse, specular, transmitted);
    VgeAccumulateCelestialDirect(1, vgeSurfaceMoonDirection, vgeSurfaceLunar, vgeSurfaceMoonRadius,
        receiver, baseColor, N, V, vge_skyVisibility, roughness, metallic, transmission,
        diffuse, specular, transmitted);
    for (int i = 0; i < clamp(int(vgeLights.lightCount), 0, vgeLights.positions.length()); ++i)
    {
        vec3 delta = vgeLights.positions[i].xyz - vge_viewPosition;
        float distanceSquared = max(dot(delta, delta), 0.0001);
        vec3 direction = toWorld * delta;
        direction *= inversesqrt(max(dot(direction, direction), 0.0001));
        addDirectLight(baseColor, N, V, direction, VgePointIlluminance(vgeLights.colors[i], distanceSquared)
            * vgeSurfaceShadowVisibility(2 + i, receiver, N),
            roughness, metallic, diffuse, specular);
    }
    diffuse = diffuse*(1.0-clamp(transmission,0.0,1.0)) + transmitted;
    // Local block illumination is a bounded fallback for these receivers, not screen-space GI
    // sampled from an unrelated opaque surface behind a transparent/first-person mesh.
    #if VGE_PBR_FORWARD_LUMON
    vec3 localDiffuse = VgeEnvironmentResponse(vge_blockIrradiance,
        baseColor,metallic,roughness,dot(N,V));
    #else
    vec3 localDiffuse = VgeEnvironmentResponse(VgeLocalEnvironment(vge_blockIrradiance, vge_sunIrradiance),
        baseColor, metallic, roughness, dot(N, V));
    #endif
    vec3 radiance = diffuse + specular + localDiffuse + VgeEmissionLuminance(baseColor, material.b);
    // Atmosphere owns air transport. Retain the engine's separate underwater medium,
    // but do not blend its distance haze over the atmospheric result a second time.
    if (vge_atmosphereAerialParams.z > .7)
        radiance = mix(radiance, VgeCompatibilityLuminance(rgbaFog.rgb), clamp(fog, 0.0, 1.0));
    else
        radiance = VgeApplyAerial(radiance, toWorld * vge_viewPosition, vge_skyVisibility, vge_atmosphereAerialParams.xyw, vgeSurfaceSunDirection);
    if (vge_pbrRoute != 0) return VgeStoreSceneLighting(radiance);
    return VgeSceneOutput(radiance, gl_FragCoord.xy, false);
}
/** Preserves non-transmitting callers which have no material transmission metadata. */
vec3 VgeForwardSurface(vec3 baseColor, vec3 N, vec3 material, float fog)
{
    return VgeForwardSurface(baseColor, N, material, fog, 0.0);
}
#endif
