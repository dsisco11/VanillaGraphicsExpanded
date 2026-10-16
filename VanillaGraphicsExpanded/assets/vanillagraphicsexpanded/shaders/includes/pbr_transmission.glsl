#ifndef VGE_PBR_TRANSMISSION_GLSL
#define VGE_PBR_TRANSMISSION_GLSL
@import "./pbr_microfacet_energy.glsl"

/** Approximates colored transmission through a thin dielectric surface; inputs point toward viewer and sun. */
vec3 VgeTransmission(vec3 baseColor, vec3 N, vec3 V, vec3 L, vec3 sunlight,
    float metallic, float strength, float visibility, float roughness)
{
    float backLighting = max(-dot(N, L), 0.0);
    // A broad forward-scattering lobe brightens surfaces viewed toward the sun without a sharp specular highlight.
    float alignment = max(dot(V, -L), 0.0);
    float scattering = 0.25 + 0.75 * alignment * alignment * alignment * alignment;
    float weight = clamp(strength, 0.0, 1.0) * (1.0 - clamp(metallic, 0.0, 1.0));
    vec3 color = clamp(baseColor,vec3(0),vec3(1));
    VgeLobeEnergy energy = VgeEvaluateMicrofacetEnergy(mix(vec3(.04),color,clamp(metallic,0.0,1.0)),
        roughness,abs(dot(N,V)));
    if(!energy.valid) return vec3(0);
    return color * (vec3(1)-energy.reflectance) * sunlight *
        (weight * backLighting * scattering * clamp(visibility, 0.0, 1.0) / 3.14159265359);
}
#endif
