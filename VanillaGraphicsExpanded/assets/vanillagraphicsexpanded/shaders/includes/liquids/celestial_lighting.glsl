#ifndef VGE_LIQUID_CELESTIAL_LIGHTING
#define VGE_LIQUID_CELESTIAL_LIGHTING
/** Retains one celestial source's attenuated incident light for reflection and medium composition. */
struct VgeLiquidCelestialLight {
    vec3 direction;
    vec3 incident;
    vec3 reflection;
};

/** Evaluates one source's shadowed scattering illumination at a signed water boundary. */
vec3 VgeLiquidBoundaryCelestialSource(int shadowSource, vec3 direction, vec3 irradiance,
    float skyVisibility, vec3 receiver, vec3 receiverNormal, vec3 outgoing, float anisotropy)
{
    if (!any(greaterThan(irradiance, vec3(0)))) return vec3(0);
    // Keep each source's incident direction and shadow slot through the phase evaluation.
    float phase = anisotropy == 0.0 ? 1.0 / 12.56637061436
        : VgeWaterAnisotropicPhase(dot(-normalize(direction), outgoing), anisotropy);
    return max(irradiance, vec3(0)) * skyVisibility
        * vgeSurfaceShadowVisibility(shadowSource, receiver, receiverNormal) * phase;
}

/** Evaluates one explicit current source against its selected rendered shadow projection. */
VgeLiquidCelestialLight VgeEvaluateLiquidCelestial(int shadowSource, vec3 direction, vec3 irradiance,
    float angularRadius, float skyVisibility, vec3 receiver, vec3 N, vec3 V, vec3 F0,
    float roughness, vec3 compensation)
{
    VgeLiquidCelestialLight light=VgeLiquidCelestialLight(vec3(0,1,0),vec3(0),vec3(0));
    if (!any(greaterThan(irradiance,vec3(0)))) return light;
    light.direction=normalize(direction);
    light.incident=max(irradiance,vec3(0))*skyVisibility;
    light.incident*=vgeSurfaceShadowVisibility(shadowSource,receiver,N);
    light.reflection=VgeFiniteDiskSpecularBRDF(N,V,light.direction,F0,roughness,angularRadius)
        *compensation*light.incident*max(dot(N,light.direction),0.0);
    return light;
}

/** Adds one source's diffuse reflection and each required liquid-medium scattering approximation. */
void VgeAccumulateLiquidCelestial(VgeLiquidCelestialLight light, vec3 N, vec3 V, vec3 waterOutgoing,
    bool fallbackSourceNeeded, bool refractedSourceNeeded, float anisotropy,
    vec3 surfaceBudget, float metallic, vec3 tint,
    inout vec3 mediumSource, inout vec3 refractedSource, inout vec3 diffuse)
{
    if (!any(greaterThan(light.incident,vec3(0)))) return;
    // Each source retains its own incident direction through both scattering paths.
    if (fallbackSourceNeeded)
        mediumSource+=light.incident*VgeWaterPhase(-light.direction,V,anisotropy);
    if (refractedSourceNeeded)
        refractedSource+=light.incident*VgeWaterPhase(-light.direction,waterOutgoing,anisotropy);
    diffuse+=surfaceBudget*(1.0-metallic)*tint*light.incident*max(dot(N,light.direction),0.0)/3.14159265359;
}
#endif
