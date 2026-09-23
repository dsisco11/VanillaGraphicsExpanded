#version 430 core
#define LUMON_TRACE_SCENE_COMPUTE 1
@import "./includes/lumon_trace_scene.glsl"
@import "./includes/lumon_surface_lighting.glsl"
layout(local_size_x=64) in;
/** One immutable geometric hit, followed by explicit radiance validity. */
struct SurfaceQuery { ivec4 cell; ivec4 normal; vec4 fraction; vec4 result; };
layout(std430,binding=0) buffer SurfaceQueries { SurfaceQuery queries[]; };
/** Rejects replaced CPU hits before resolving the shared captured face identity. */
void main()
{
    uint i=gl_GlobalInvocationID.x;
    if (i>=uint(queries.length())) return;
    queries[i].result=vec4(0);
    ivec3 cell=queries[i].cell.xyz, n=queries[i].normal.xyz;
    uint geometry;
    if (lumonTraceSceneReadGeometry(cell,TRACE_SCENE_SURFACE,geometry)!=TRACE_SCENE_READY || (geometry&3u)!=2u) return;
    uvec4 faces=texelFetch(traceSceneFaces,ivec2(int(geometry>>2),0),0);
    if (queries[i].cell.w!=0 && (faces.w>>2)!=uint(queries[i].cell.w)) return;
    uint face=n.x>0?1u:n.x<0?3u:n.y>0?4u:n.y<0?5u:n.z>0?2u:0u;
    uint surface;
    vec3 radiance;
    if (lumonTraceSceneReadSurface(cell,face,surface) &&
        sampleSurfaceLighting(cell,n,queries[i].fraction.xyz,surface,radiance))
        queries[i].result=vec4(radiance,1);
}
