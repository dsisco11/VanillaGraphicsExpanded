#ifndef SURFACE_FALLBACK_GLSL
#define SURFACE_FALLBACK_GLSL
/** Bounded full-texel retries; integer cells avoid loss of precision at large world coordinates. */
struct SurfaceFallbackRequest { uvec4 identity; ivec4 cellSeed; vec4 fractionRays; vec4 normal; };
layout(std430,binding=5) buffer SurfaceFallbackRequests
{
    uvec4 fallbackHeader; // count, capacity, submitted pages, rotating selection
    SurfaceFallbackRequest fallbackRequests[];
};
/** Already validated complete estimates; absent records never touch retained texels. */
struct SurfaceFallbackCommit { uvec4 identity; vec4 estimate; };
layout(std430,binding=6) readonly buffer SurfaceFallbackCommits { SurfaceFallbackCommit fallbackCommits[]; };

/** Rotates bounded per-page opportunities, rather than always admitting the first lanes of a large tile. */
void surfaceQueueFallback(uint wi, uvec4 item, uint linear, uint batchCount, ivec3 cell, vec3 fraction, vec3 normal)
{
    if (fallbackHeader.y == 0u || fallbackHeader.z == 0u || lighting.sampling.y > 64u) return;
    uint pages = min(fallbackHeader.y, fallbackHeader.z);
    if ((wi + fallbackHeader.w) % fallbackHeader.z >= pages) return;
    uint quota = fallbackHeader.y / pages;
    uint bucket = item.z % batchCount;
    uint size = (lighting.layoutInfo.x * lighting.layoutInfo.x - bucket + batchCount - 1u) / batchCount;
    // Hash the collection serial so cycling lighting buckets cannot lock admission to one texel subset.
    uint offset = Squirrel3HashU(fallbackHeader.w,item.x,bucket) % size;
    if ((linear / batchCount + offset) % size >= quota) return;
    uint index = atomicAdd(fallbackHeader.x,1u);
    if (index >= fallbackHeader.y) return;
    fallbackRequests[index].identity = uvec4(item.x,item.y,item.w & 0x3fffffffu,linear);
    fallbackRequests[index].cellSeed = ivec4(cell,int(Squirrel3HashU(item.x,linear,lighting.sampling.w)));
    fallbackRequests[index].fractionRays = vec4(fraction,float(max(1u,lighting.sampling.y)));
    fallbackRequests[index].normal = vec4(normal,0);
}

/** Applies the same bounded-history formula to ordinary GPU samples and validated fallback estimates. */
bool surfaceAccumulate(ivec3 address, vec3 estimate)
{
    if (any(isnan(estimate)) || any(isinf(estimate))) return false;
    vec4 previous = imageLoad(indirectIrradiance,address);
    if (any(isnan(previous)) || any(isinf(previous))) previous = vec4(0);
    float previousWeight = max(0.0,previous.a);
    float weight = min(previousWeight+1.0,float(max(1u,lighting.policy.z)));
    float alpha = 1.0/(1.0+previousWeight);
    imageStore(indirectIrradiance,address,vec4(clamp(mix(previous.rgb,estimate,alpha),vec3(0),vec3(65504)),weight));
    return true;
}
#endif
