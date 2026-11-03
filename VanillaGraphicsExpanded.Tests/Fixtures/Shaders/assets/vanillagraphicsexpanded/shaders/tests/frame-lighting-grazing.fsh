#version 450 core
@import "vanillagraphicsexpanded:shaders/includes/pbr_scene_storage.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_common.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_microfacet_energy.glsl"

layout(std140, binding = 28) uniform GrazingProbeInputs
{
    int highlight;
};

layout(location = 0) out vec4 result;

/** Applies the original two-pixel grazing workload to the production scene-storage and BRDF helpers. */
void main()
{
    vec3 radiance = vec3(.0003);
    if (int(gl_FragCoord.x) == 0 && highlight != 0)
    {
        vec3 V = vec3(1, 0, 1e-7), L = vec3(-1, 0, 1e-7);
        VgeLobeEnergy energy = VgeEvaluateMicrofacetEnergy(vec3(1), .04, 1e-7);
        radiance = cookTorranceBRDF(vec3(0, 0, 1), V, L, vec3(1), .04)
            * energy.compensation * 1e-7 * 1e7;
    }
    result = vec4(VgeStoreSceneLighting(radiance), 1);
}
