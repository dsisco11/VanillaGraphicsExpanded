#version 430 core
@import "../cloud_lighting_volume/parameters.glsl"
@import "../cloud_lighting_volume/density.glsl"
@import "../cloud_lighting_volume/lookup.glsl"
layout(local_size_x=1) in;
layout(std140) uniform LightingVolumeQuery { vec4 point; };
layout(std430) buffer LightingVolumeResult { vec4 result[]; };
/** Runs production candidate helpers at one authored native-local position. */
void main()
{
    ivec2 cell=ivec2(floor(point.xz/50.0));
    float density=CandidateDensity(point.xyz,cell,CandidateColumn(cell));
    result[0]=vec4(density,CandidateTransmission(point.xyz,0),CandidateTransmission(point.xyz,1),0);
    result[1]=vec4(CandidateDiffuse(point.xyz,vec3(2,4,6)),0);
}
