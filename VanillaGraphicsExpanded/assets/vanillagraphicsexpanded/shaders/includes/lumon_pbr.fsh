#ifndef LUMON_PBR_FSH
#define LUMON_PBR_FSH
// ═══════════════════════════════════════════════════════════════════════════
// LumOn PBR Utility Functions
// ═══════════════════════════════════════════════════════════════════════════
// Shared helpers for physically-plausible indirect compositing.

// Pull in generic BRDF helpers that LumOn's composite logic depends on.
@import "./pbrfunctions.fsh"

// Pull in LumOn material sampling/F0 helpers that this file depends on.
@import "./lumon_material.fsh"
//
//

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
    // Indirect lighting is an integrated term; we use a stable N·V estimate.
    // Primary is reconstructed viewDir; fallback is view-space +Z (camera-to-fragment).
    float viewNdotV = dot(normalVS, viewDirVS);
    float approxNdotV = normalVS.z;
    float NdotV = clamp(max(viewNdotV, approxNdotV), 0.0, 1.0);

    vec3 F0 = lumonComputeF0(albedo, metallic);
    vec3 F = fresnelSchlick(NdotV, F0);

    vec3 kD = pbrDiffuseFactorFromFresnel(F, metallic);
    vec3 kS = pbrSpecularFactorFromFresnel(F);

    float r = clamp(roughness, 0.0, 1.0);

    // Without a prefiltered environment, we apply a conservative roughness attenuation
    // to specular energy to avoid over-bright mirror-like responses.
    float specRoughnessAtten = 1.0 - r;

    float aoClamped = clamp(ao, 0.0, 1.0);
    float diffuseAO = mix(1.0, aoClamped, clamp(diffuseAOStrength, 0.0, 1.0));
    float specAO = mix(1.0, aoClamped, clamp(specularAOStrength, 0.0, 1.0) * specRoughnessAtten);

    outIndirectDiffuse = indirectRadiance * albedo * kD * diffuseAO;
    outIndirectSpecular = indirectRadiance * kS * specRoughnessAtten * specAO;
}

#endif // LUMON_PBR_FSH
