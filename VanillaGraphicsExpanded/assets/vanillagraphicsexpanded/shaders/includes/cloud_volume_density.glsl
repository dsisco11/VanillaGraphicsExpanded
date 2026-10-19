#ifndef VGE_CLOUD_VOLUME_DENSITY
#define VGE_CLOUD_VOLUME_DENSITY
uniform sampler2D cloudMap;

/** Rejects invalid extinction/bounds and never extends native clamp-to-edge storage. */
vec4 CloudColumn(ivec2 cell)
{
    int width=int(volumeOffsetWidth.w);
    if(any(lessThan(cell,ivec2(0)))||any(greaterThanEqual(cell,ivec2(width)))) return vec4(0);
    vec4 data=texelFetch(cloudMap,cell,0);
    if(any(isnan(data))||any(isinf(data))||data.r<0.0||data.a<=data.b) return vec4(0);
    return data;
}
/** Tests whether a neighbor covers this height, avoiding seams on occupied shared faces. */
bool CloudOccupied(ivec2 cell,float y)
{
    vec4 data=CloudColumn(cell);
    if(data.r<=0.0) return false;
    if(y<data.b*50.0) return false;
    if(y>data.a*50.0) return false;
    return true;
}
/** Retains only descriptors for consecutive samples in one native column; occupancy remains height dependent. */
struct CloudDensityCache
{
    bool valid;
    ivec2 cell;
    vec4 column;
};
/** Starts a ray-local descriptor cache without reading the native map or assuming occupied neighbors. */
CloudDensityCache CloudDensityCacheStart()
{
    return CloudDensityCache(false,ivec2(0),vec4(0));
}
/** Fetches the current column only when the scheduled ray enters a different native cell. */
vec4 CloudDensityColumn(ivec2 cell,inout CloudDensityCache cache)
{
    if(!cache.valid || any(notEqual(cache.cell,cell)))
    {
        cache.valid=true; cache.cell=cell; cache.column=CloudColumn(cell);
    }
    return cache.column;
}
/** Fetches only a face capable of reducing erosion, without retaining four descriptors across shadow marches. */
vec4 CloudDensityNeighbor(int face,CloudDensityCache cache)
{
    ivec2 offset=face==0?ivec2(-1,0):face==1?ivec2(1,0):face==2?ivec2(0,-1):ivec2(0,1);
    return CloudColumn(cache.cell+offset);
}
/** Samples all overlapping octaves once with explicit LOD in compute and divergent ray loops. */
uniform sampler3D cloudPerturbation;
float CloudNoise(vec3 material)
{
    // Whole repeats preserve the 12800-unit material/recenter period. Texture nodes
    // store i/64, so the half-texel offset maps those nodes to hardware texel centers.
    float repeats=max(1.0,floor(volumeDetail.z*(12800.0/8.0)+.5));
    vec3 uv=material*(repeats/12800.0)+vec3(.5/64.0);
    vec4 octaves=textureLod(cloudPerturbation,uv,0.0);
    return clamp(dot(octaves,vec4(8.0,4.0,2.0,1.0)/15.0),0.0,1.0);
}
/** Evaluates the shared density field while reusing descriptors, never height-dependent occupancy decisions. */
float CloudDensity(vec3 local,ivec2 cell,vec4 data,inout CloudDensityCache cache)
{
    if(data.r<=0.0||local.y<data.b*50.0||local.y>data.a*50.0) return 0.0;
    float detail=1.0;
    if(volumeDetail.w>.5)
    {
        // Callers with an already fetched descriptor can enter the same cache contract directly.
        if(!cache.valid || any(notEqual(cache.cell,cell)))
        {
            cache.valid=true; cache.cell=cell; cache.column=data;
        }
        vec2 f=local.xz-vec2(cell)*50.0;
        float edge=min(local.y-data.b*50.0,data.a*50.0-local.y);
        // A farther face cannot reduce the current minimum; beyond the erosion width it is neutral.
        // Neighbor occupancy is tested at the current height; no sampled face decision survives it.
        if(f.x<min(edge,volumeDetail.y))
        {
            vec4 neighbor=CloudDensityNeighbor(0,cache);
            if(neighbor.r<=0.0 || local.y<neighbor.b*50.0 || local.y>neighbor.a*50.0) edge=min(edge,f.x);
        }
        if(50.0-f.x<min(edge,volumeDetail.y))
        {
            vec4 neighbor=CloudDensityNeighbor(1,cache);
            if(neighbor.r<=0.0 || local.y<neighbor.b*50.0 || local.y>neighbor.a*50.0) edge=min(edge,50.0-f.x);
        }
        if(f.y<min(edge,volumeDetail.y))
        {
            vec4 neighbor=CloudDensityNeighbor(2,cache);
            if(neighbor.r<=0.0 || local.y<neighbor.b*50.0 || local.y>neighbor.a*50.0) edge=min(edge,f.y);
        }
        if(50.0-f.y<min(edge,volumeDetail.y))
        {
            vec4 neighbor=CloudDensityNeighbor(3,cache);
            if(neighbor.r<=0.0 || local.y<neighbor.b*50.0 || local.y>neighbor.a*50.0) edge=min(edge,50.0-f.y);
        }
        if(edge<=0.0) return 0.0;
        float variation=1.0;
        if(volumeDetail.x>0.0)
        {
            vec3 material=local+vec3(volumeAnchorUnits.x,0,volumeAnchorUnits.y);
            variation=1.0+volumeDetail.x*(2.0*CloudNoise(material)-1.0);
        }
        detail=variation*smoothstep(0.0,volumeDetail.y,edge);
    }
    // Native r is extinction per fifty world units, including producer fading/perception.
    return (data.r/50.0)/volumeAnchorUnits.z*detail;
}
/** Evaluates independent density queries without retaining descriptors across unrelated samples. */
float CloudDensity(vec3 local,ivec2 cell,vec4 data)
{
    CloudDensityCache cache=CloudDensityCacheStart();
    return CloudDensity(local,cell,data,cache);
}
/** Intersects one axis robustly, including rays exactly parallel to a column face. */
bool CloudSlab(float o,float d,float lo,float hi,inout float near,inout float far)
{
    if(abs(d)<1e-8)
    {
        if(o<lo) return false;
        return o<=hi;
    }
    vec2 t=vec2(lo-o,hi-o)/d;
    near=max(near,min(t.x,t.y)); far=min(far,max(t.x,t.y));
    return far>near;
}
#endif
