#version 450 core
#extension GL_ARB_shader_storage_buffer_object : require
#extension GL_ARB_shader_image_load_store : require
#extension GL_ARB_shading_language_420pack : require
layout(std140, binding = 28) uniform TestInputs
{
    int vge_pbrRoute;
    int scenario;
};

vec3 vge_viewPosition = vec3(0, 0, -2);
vec3 vge_blockIrradiance = vec3(0);
vec3 vge_sunIrradiance = vec3(0.2, 0.4, 0.8);
vec3 vge_atmosphereEnvironment = vec3(0);
vec4 vge_atmosphereAerialParams = vec4(.001, 0, 0, 16);
float vge_skyVisibility = 1.0;
vec4 rgbaFog = vec4(0);
layout(location = 0) out vec4 outColor;

// Match the production import dependency order after declaring every helper input.
@import "vanillagraphicsexpanded:shaders/includes/atmosphere_aerial.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_color.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_common.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_photometric.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_direct_brdf.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_environment.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_forward_surface.glsl"

/** Evaluates the production forward helper with the complete authored numerical scenario set. */
void main()
{
    if (scenario == 5 || scenario >= 7)
        vge_sunIrradiance = vec3(0);
    if (scenario == 13) vge_skyVisibility = 0.0;

    vec3 normal = scenario == 3 ? vec3(1, 0, 0) : vec3(0, 0, 1);
    float emission = scenario == 1 || scenario == 9 || scenario == 15 ? 2.0 : 0.0;
    outColor = vec4(VgeForwardSurface(vec3(0.2, 0.4, 0.6), normal,
        vec3(0.5, 0, emission), 0.0), 0.37);
}
