#version 430 core
@import "./includes/lumon_octahedral.glsl"
layout(local_size_x=64) in;
struct Answer { ivec4 status; ivec4 cell; ivec4 normal; vec4 fraction; vec4 radiance; };
layout(std430,binding=0) readonly buffer ResidentAnswers { Answer answers[]; };
struct Sample { ivec4 target; vec4 value; };
layout(std430,binding=1) readonly buffer Commit {
    ivec4 probe; vec4 ao; vec4 scalars; Sample samples[];
};
layout(rgba16f,binding=0) writeonly uniform image2D radianceAtlas;
layout(rgba16f,binding=1) writeonly uniform image2D visibilityAtlas;
layout(rg16f,binding=2) writeonly uniform image2D distanceAtlas;
layout(rg32f,binding=3) writeonly uniform image2D metadataAtlas;
/** Writes every admitted ready direction before exposing this admission's valid metadata. */
void main()
{
    for(uint i=gl_LocalInvocationIndex;i<uint(probe.w);i+=64u)
    {
        Sample entry=samples[i];
        vec4 value=entry.value;
        if(entry.target.z>=0) value.xyz=max(vec3(0),answers[entry.target.z].radiance.xyz);
        imageStore(radianceAtlas,probe.xy*probe.z+entry.target.xy,value);
    }
    memoryBarrierImage(); barrier();
    if(gl_LocalInvocationIndex==0u)
    {
        imageStore(visibilityAtlas,probe.xy,vec4(lumonDirectionToOctahedralUV(normalize(ao.xyz)),scalars.x,ao.w));
        imageStore(distanceAtlas,probe.xy,vec4(scalars.z,0,0,0));
        imageStore(metadataAtlas,probe.xy,vec4(scalars.y,scalars.w,0,0));
    }
}
