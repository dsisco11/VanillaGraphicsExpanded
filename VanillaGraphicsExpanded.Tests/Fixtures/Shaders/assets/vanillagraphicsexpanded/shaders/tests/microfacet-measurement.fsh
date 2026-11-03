#version 450 core
@import "vanillagraphicsexpanded:shaders/includes/pbr_common.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_microfacet_energy.glsl"

layout(std140, binding = 28) uniform MicrofacetMeasurementInputs
{
    int evaluations;
};

layout(location = 0) out vec4 result;

/** Retains the paired warm Cook-Torrance cost workload and varies roughness per evaluation. */
void main()
{
    vec2 uv = gl_FragCoord.xy / 256.0;
    vec3 N = vec3(0, 0, 1), V = normalize(vec3(uv * 1.8 - .9, 1));
    vec3 total = vec3(0);
    for (int i = 0; i < evaluations; ++i)
    {
        float index = float(i);
        vec3 L = normalize(vec3(sin(index * .31), cos(index * .19), 1));
        float roughness = clamp(.04 + .96 * uv.x + .001 * sin(index), .04, 1.0);
        vec3 value = cookTorranceBRDF(N, V, L, vec3(.04, .5, 1), roughness);
#if VGE_MICROFACET_MEASUREMENT_COMPENSATED
        value *= VgeEvaluateMicrofacetEnergy(vec3(.04, .5, 1), roughness, V.z).compensation;
#endif
        total += value * L.z;
    }
    result = vec4(total, 1);
}
