#version 450 core
@import "vanillagraphicsexpanded:shaders/includes/pbr_finite_disk_brdf.glsl"
layout(std140, binding=28) uniform SolarBrdfInputs
{
    vec4 normal;
    vec4 view;
    vec4 light;
    float roughness;
    float radius;
    int mode;
};
layout(location=0) out vec4 result;
/** Evaluates the selected directional, finite-disk, or representative-direction BRDF case. */
void main(){
    vec3 value = mode == 2 ? VgeFiniteDiskRepresentativeDirection(normal.xyz,view.xyz,light.xyz,radius)
        : mode == 1 ? cookTorranceBRDF(normal.xyz,view.xyz,light.xyz,vec3(.04,.5,1),roughness)
        : VgeFiniteDiskSpecularBRDF(normal.xyz,view.xyz,light.xyz,vec3(.04,.5,1),roughness,radius);
    result=vec4(value,1);
}
