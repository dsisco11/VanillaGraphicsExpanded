#ifndef VGE_LIGHTING_VOLUME_DENSITY
#define VGE_LIGHTING_VOLUME_DENSITY
uniform sampler2D candidateMap;

/** Rejects invalid extinction/bounds and never extends native clamp-to-edge storage. */
vec4 CandidateColumn(ivec2 cell)
{
    int width=int(candidateOffsetWidth.w);
    if(any(lessThan(cell,ivec2(0)))||any(greaterThanEqual(cell,ivec2(width)))) return vec4(0);
    vec4 data=texelFetch(candidateMap,cell,0);
    if(any(isnan(data))||any(isinf(data))||data.r<0.0||data.a<=data.b) return vec4(0);
    return data;
}
/** Tests whether a neighbor covers this height, avoiding seams on occupied shared faces. */
bool CandidateOccupied(ivec2 cell,float y)
{
    vec4 data=CandidateColumn(cell);
    if(data.r<=0.0) return false;
    if(y<data.b*50.0) return false;
    if(y>data.a*50.0) return false;
    return true;
}
/** Retains only descriptors for consecutive samples in one native column; occupancy remains height dependent. */
struct CandidateDensityCache
{
    bool valid;
    ivec2 cell;
    vec4 column;
};
/** Starts a ray-local descriptor cache without reading the native map or assuming occupied neighbors. */
CandidateDensityCache CandidateDensityCacheStart()
{
    return CandidateDensityCache(false,ivec2(0),vec4(0));
}
/** Fetches the current column only when the scheduled ray enters a different native cell. */
vec4 CandidateDensityColumn(ivec2 cell,inout CandidateDensityCache cache)
{
    if(!cache.valid || any(notEqual(cache.cell,cell)))
    {
        cache.valid=true; cache.cell=cell; cache.column=CandidateColumn(cell);
    }
    return cache.column;
}
/** Fetches only a face capable of reducing erosion, without retaining four descriptors across shadow marches. */
vec4 CandidateDensityNeighbor(int face,CandidateDensityCache cache)
{
    ivec2 offset=face==0?ivec2(-1,0):face==1?ivec2(1,0):face==2?ivec2(0,-1):ivec2(0,1);
    return CandidateColumn(cache.cell+offset);
}
/** Mixes a corner key using integer arithmetic, without transcendental hash evaluation. */
float CandidateHash(uint key)
{
    key^=key>>16u; key*=0x7feb352du;
    key^=key>>15u; key*=0x846ca68bu;
    key^=key>>16u;
    return float(key>>8u)*(1.0/16777216.0);
}
/** Interpolates one bounded periodic field, sharing lattice wrapping and coordinate keys across its corners. */
float CandidateNoise(vec3 material)
{
    float period=max(1.0,floor(0.08*12800.0+.5));
    vec3 p=material*(period/12800.0), base=floor(p), f=fract(p);
    f=f*f*(3.0-2.0*f);
    // Wrap the lower corner once. The upper corner is either its successor or the
    // periodic origin, including negative material coordinates and one-cell periods.
    uvec3 lower=uvec3(mod(base,vec3(period))), upper=lower+uvec3(1);
    upper=uvec3(upper.x>=uint(period)?0u:upper.x,
        upper.y>=uint(period)?0u:upper.y,upper.z>=uint(period)?0u:upper.z);
    uvec3 a=lower*uvec3(0x9e3779b9u,0x85ebca6bu,0xc2b2ae35u);
    uvec3 b=upper*uvec3(0x9e3779b9u,0x85ebca6bu,0xc2b2ae35u);
    // Reuse the six coordinate products; only the final avalanche differs per corner.
    return mix(mix(mix(CandidateHash(a.x^a.y^a.z^0x434c4f55u),CandidateHash(b.x^a.y^a.z^0x434c4f55u),f.x),
        mix(CandidateHash(a.x^b.y^a.z^0x434c4f55u),CandidateHash(b.x^b.y^a.z^0x434c4f55u),f.x),f.y),
        mix(mix(CandidateHash(a.x^a.y^b.z^0x434c4f55u),CandidateHash(b.x^a.y^b.z^0x434c4f55u),f.x),
        mix(CandidateHash(a.x^b.y^b.z^0x434c4f55u),CandidateHash(b.x^b.y^b.z^0x434c4f55u),f.x),f.y),f.z);
}
/** Evaluates the shared density field while reusing descriptors, never height-dependent occupancy decisions. */
float CandidateDensity(vec3 local,ivec2 cell,vec4 data,inout CandidateDensityCache cache)
{
    if(data.r<=0.0||local.y<data.b*50.0||local.y>data.a*50.0) return 0.0;
    float detail=1.0;
    #if VGE_LIGHTING_VOLUME_DETAIL
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
        if(f.x<min(edge,2.0))
        {
            vec4 neighbor=CandidateDensityNeighbor(0,cache);
            if(neighbor.r<=0.0 || local.y<neighbor.b*50.0 || local.y>neighbor.a*50.0) edge=min(edge,f.x);
        }
        if(50.0-f.x<min(edge,2.0))
        {
            vec4 neighbor=CandidateDensityNeighbor(1,cache);
            if(neighbor.r<=0.0 || local.y<neighbor.b*50.0 || local.y>neighbor.a*50.0) edge=min(edge,50.0-f.x);
        }
        if(f.y<min(edge,2.0))
        {
            vec4 neighbor=CandidateDensityNeighbor(2,cache);
            if(neighbor.r<=0.0 || local.y<neighbor.b*50.0 || local.y>neighbor.a*50.0) edge=min(edge,f.y);
        }
        if(50.0-f.y<min(edge,2.0))
        {
            vec4 neighbor=CandidateDensityNeighbor(3,cache);
            if(neighbor.r<=0.0 || local.y<neighbor.b*50.0 || local.y>neighbor.a*50.0) edge=min(edge,50.0-f.y);
        }
        if(edge<=0.0) return 0.0;
        float variation=1.0;
        if(0.15>0.0)
        {
            vec3 material=local+vec3(candidateAnchorUnits.x,0,candidateAnchorUnits.y);
            variation=1.0+0.15*(2.0*CandidateNoise(material)-1.0);
        }
        detail=variation*smoothstep(0.0,2.0,edge);
    }
    #endif
    // Native r is extinction per fifty world units, including producer fading/perception.
    return (data.r/50.0)/candidateAnchorUnits.z*detail;
}
/** Evaluates independent density queries without retaining descriptors across unrelated samples. */
float CandidateDensity(vec3 local,ivec2 cell,vec4 data)
{
    CandidateDensityCache cache=CandidateDensityCacheStart();
    return CandidateDensity(local,cell,data,cache);
}
/** Intersects one axis robustly, including rays exactly parallel to a column face. */
bool CandidateSlab(float o,float d,float lo,float hi,inout float near,inout float far)
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

