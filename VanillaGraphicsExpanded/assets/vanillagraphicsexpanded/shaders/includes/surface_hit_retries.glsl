#ifndef SURFACE_HIT_RETRIES_GLSL
#define SURFACE_HIT_RETRIES_GLSL
/** Exact integer hit queries; all rays must resolve geometry before this texel can be retained. */
struct SurfaceHitQuery { ivec4 cell; ivec4 normal; vec4 fraction; vec4 result; };
struct SurfaceHitCapture { SurfaceFallbackRequest request; uvec4 state; SurfaceHitQuery queries[64]; };
layout(std430,binding=7) buffer SurfaceHitRetries
{
    uvec4 hitHeader;
    uvec4 hitPendingHeader;
    uvec4 hitPending[32];
    SurfaceHitCapture hitCaptures[];
};

/** Retained geometry suppresses only indirect retracing; direct seed and refresh never consult this list. */
bool surfaceHitPending(uvec4 identity)
{
    for (uint i=0u; i<min(hitPendingHeader.x,32u); i++)
        if (all(equal(hitPending[i],identity))) return true;
    return false;
}

/** Selects bounded opportunities without per-thread ray arrays or allocating for every lighting miss. */
int surfaceBeginHitCapture(uint wi, uvec4 item, uint linear, uint batchCount)
{
    if (lighting.slotDimensions.w == 0 || hitHeader.y == 0u || hitHeader.z == 0u || lighting.sampling.y > 64u) return -1;
    uint pages = min(hitHeader.y,hitHeader.z);
    if ((wi+hitHeader.w)%hitHeader.z >= pages) return -1;
    uint quota = hitHeader.y/pages;
    uint bucket = item.z%batchCount;
    uint size = (lighting.layoutInfo.x*lighting.layoutInfo.x-bucket+batchCount-1u)/batchCount;
    uint offset = Squirrel3HashU(hitHeader.w,item.x,bucket)%size;
    if ((linear/batchCount+offset)%size >= quota) return -1;
    uint index = atomicAdd(hitHeader.x,1u);
    if (index >= hitHeader.y) return -1;
    hitCaptures[index].state = uvec4(0);
    hitCaptures[index].request.identity = uvec4(item.x,item.y,item.w&0x1fffffffu,linear);
    hitCaptures[index].request.cellSeed = ivec4(0);
    hitCaptures[index].request.fractionRays = vec4(0,0,0,float(max(1u,lighting.sampling.y)));
    hitCaptures[index].request.normal = vec4(0);
    return int(index);
}

/** Records both ready and unready hits, so the later lookup always uses the original complete ray denominator. */
void surfaceRecordHit(int index, uint ordinal, LumonTraceSceneHit hit, vec3 radiance, bool ready)
{
    if (index < 0) return;
    uvec4 faces = texelFetch(traceSceneFaces,ivec2(int(hit.material),0),0);
    hitCaptures[index].queries[ordinal].cell = ivec4(hit.cell,int(faces.w>>2u));
    hitCaptures[index].queries[ordinal].normal = ivec4(hit.normal,0);
    hitCaptures[index].queries[ordinal].fraction = vec4(hit.fraction,0);
    hitCaptures[index].queries[ordinal].result = ready ? vec4(radiance,1) : vec4(0);
}
#endif
