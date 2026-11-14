#ifndef VGE_PBR_CELESTIAL_DIRECT_GLSL
#define VGE_PBR_CELESTIAL_DIRECT_GLSL
/** Adds one explicit celestial source using current illumination and its selected rendered shadow mapping. */
void VgeAccumulateCelestialDirect(int shadowSource, vec3 direction, vec3 irradiance, float angularRadius,
    vec3 receiver, vec3 baseColor, vec3 N, vec3 V, float skyVisibility,
    float roughness, float metallic, float transmission,
    inout vec3 diffuse, inout vec3 specular, inout vec3 transmitted)
{
    if (!any(greaterThan(irradiance, vec3(0)))) return;
    vec3 L = normalize(direction);
    float visibility = vgeSurfaceShadowVisibility(shadowSource, receiver, N);
    vec3 incident = irradiance * skyVisibility;
    addFiniteDiskDirectLight(baseColor, N, V, L, incident * visibility,
        roughness, metallic, angularRadius, diffuse, specular);
    transmitted += VgeTransmission(baseColor, N, V, L, incident,
        metallic, transmission, visibility, roughness);
}
#endif
