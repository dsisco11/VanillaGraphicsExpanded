#ifndef LUMON_PBR_FSH
#define LUMON_PBR_FSH
// ═══════════════════════════════════════════════════════════════════════════
// LumOn PBR Utility Functions
// ═══════════════════════════════════════════════════════════════════════════
// Shared helpers for physically-plausible indirect compositing.

// Pull in generic BRDF helpers that LumOn's composite logic depends on.
@import "./pbr_environment.glsl"
@import "./pbr_indirect_occlusion.glsl"

// Pull in LumOn material sampling/F0 helpers that this file depends on.
@import "./lumon_material.glsl"
//
//

/** Splits indirect radiance and applies shared diffuse/specular visibility once using the actual receiver normal and view. */
void lumonComputeIndirectSplit(
    vec3 indirectRadiance,
    vec3 albedo,
    vec3 normalVS,
    vec3 viewDirVS,
    float roughness,
    float metallic,
    float ao,
    float diffuseAOStrength,
    float specularAOStrength,
    out vec3 outIndirectDiffuse,
    out vec3 outIndirectSpecular)
{
    float nDotV=clamp(dot(normalVS,viewDirVS),0.0,1.0);
    VgeEnvironmentSplit(indirectRadiance,albedo,metallic,roughness,nDotV,outIndirectDiffuse,outIndirectSpecular);
    vec2 visibility=VgeIndirectVisibility(ao,roughness,nDotV,diffuseAOStrength,specularAOStrength);
    outIndirectDiffuse*=visibility.x;
    outIndirectSpecular*=visibility.y;
}

#endif // LUMON_PBR_FSH
