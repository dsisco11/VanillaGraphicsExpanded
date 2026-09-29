#ifndef VGE_PBR_FOLIAGE_TRANSMISSION_GLSL
#define VGE_PBR_FOLIAGE_TRANSMISSION_GLSL

/** Approximates colored transmission through a thin dielectric leaf; inputs point toward viewer and sun. */
vec3 VgeFoliageTransmission(vec3 baseColor, vec3 N, vec3 V, vec3 L, vec3 sunlight,
    float metallic, float strength, float visibility)
{
    float backLighting = max(-dot(N, L), 0.0);
    // A broad forward-scattering lobe brightens leaves viewed toward the sun without a sharp specular highlight.
    float alignment = max(dot(V, -L), 0.0);
    float scattering = 0.25 + 0.75 * alignment * alignment * alignment * alignment;
    float weight = clamp(strength, 0.0, 1.0) * (1.0 - clamp(metallic, 0.0, 1.0));
    return clamp(baseColor, vec3(0.0), vec3(1.0)) * sunlight *
        (weight * backLighting * scattering * clamp(visibility, 0.0, 1.0) / 3.14159265359);
}
#endif
