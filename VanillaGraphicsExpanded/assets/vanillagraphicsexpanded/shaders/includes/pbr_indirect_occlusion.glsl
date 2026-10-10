#ifndef VGE_PBR_INDIRECT_OCCLUSION_GLSL
#define VGE_PBR_INDIRECT_OCCLUSION_GLSL
/** Derives bounded indirect specular visibility from scalar AO, perceptual roughness and the actual receiver/view angle.
 * Uses the empirical Lagarde approximation described in Filament's specular occlusion section.
 * This is not directional visibility or a bent normal, and it never attenuates direct lighting.
 */
float VgeIndirectSpecularVisibility(float visibility, float roughness, float nDotV)
{
    float ao=clamp(visibility,0.0,1.0);
    float r=clamp(roughness,0.0,1.0);
    float nv=clamp(nDotV,0.0,1.0);
    return clamp(pow(nv+ao,exp2(-16.0*r-1.0))-1.0+ao,0.0,1.0);
}
/** Applies independent bounded strengths to diffuse and specular visibility exactly once at indirect composition. */
vec2 VgeIndirectVisibility(float visibility, float roughness, float nDotV, float diffuseStrength, float specularStrength)
{
    return vec2(mix(1.0,clamp(visibility,0.0,1.0),clamp(diffuseStrength,0.0,1.0)),
        mix(1.0,VgeIndirectSpecularVisibility(visibility,roughness,nDotV),clamp(specularStrength,0.0,1.0)));
}
#endif
