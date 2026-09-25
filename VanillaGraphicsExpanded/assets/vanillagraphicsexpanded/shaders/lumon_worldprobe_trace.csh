#version 430 core
#define LUMON_TRACE_SCENE_COMPUTE 1
@import "./includes/lumon_trace_scene_trace.glsl"
@import "./includes/lumon_surface_lighting.glsl"
@import "./includes/lumon_octahedral.glsl"
@import "./includes/lumon_worldprobe_trace_work.glsl"
layout(local_size_x=8,local_size_y=8) in;
/** Geometry and lighting output only; input data is shared per probe. */
struct WorldProbeAnswer { ivec4 status; ivec4 cell; ivec4 normal; vec4 hitFractionDistance; vec4 radiance; };
layout(std430,binding=0) writeonly buffer WorldProbeAnswers { WorldProbeAnswer answers[]; };
layout(std430,binding=5) readonly buffer WorldProbeTraceTiles { uvec2 tiles[]; };
layout(std430,binding=6) readonly buffer WorldProbeSelectedDirections { uint selectedDirections[]; };

/** Generates each direction on the GPU from its atlas texel or cardinal importance selector. */
void main()
{
    uvec2 tile=tiles[gl_WorkGroupID.x];
    WorldProbeTraceProbe probe=probes[tile.x];
    uint local=tile.y+gl_LocalInvocationIndex;
    if(local>=probe.selection.z) return;
    uint index=probe.selection.y+local;
    uint selected=selectedDirections[index];
    vec3 direction;
    float distance=probe.fractionDistance.w;
    if((selected&0x80000000u)!=0u)
    {
        uint cardinal=selected&7u;
        direction=vec3(0);
        direction[cardinal>>1u]=(cardinal&1u)==0u?1.0:-1.0;
        distance=probe.nearbyDistance.x;
    }
    else
    {
        uvec2 texel=uvec2(selected%probe.selection.x,selected/probe.selection.x);
        direction=lumonOctahedralUVToDirection((vec2(texel)+vec2(0.5))/float(probe.selection.x));
    }
    LumonTraceSceneHit hit=lumonTraceSceneEndpoint(probe.origin.xyz,probe.fractionDistance.xyz,
        direction,distance,int(probe.selection.w),TRACE_SCENE_SURFACE,probe.origin.w,true);
    answers[index].status=ivec4(hit.outcome,hit.reason,0,0);
    answers[index].cell=ivec4(hit.cell,0);
    answers[index].normal=ivec4(hit.normal,0);
    answers[index].hitFractionDistance=vec4(hit.fraction,hit.distance);
    answers[index].radiance=vec4(0);
    if(hit.outcome!=LUMON_NEAR_FIELD_HIT) return;
    uvec4 faces=texelFetch(traceSceneFaces,ivec2(int(hit.material),0),0);
    answers[index].cell.w=int(faces.w>>2);
    ivec3 n=hit.normal;
    uint face=n.x>0?1u:n.x<0?3u:n.y>0?4u:n.y<0?5u:n.z>0?2u:0u;
    uint surface;
    vec3 radiance;
    if(lumonTraceSceneReadSurface(hit.cell,face,surface) && sampleSurfaceLighting(hit.cell,n,hit.fraction,surface,radiance))
        answers[index].radiance=vec4(radiance,1);
}
