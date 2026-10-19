#version 430 core
layout(local_size_x=64) in;
layout(std140) uniform LightingWorkInputs
{
    ivec4 workExtent; // texture size, native width, region side, interior cells
    ivec4 workAlignment; // first interior key modulo four, circular native origin modulo width
    ivec4 workHistory; // previous native origin relative to current, history valid, diffuse enabled
    ivec4 workDelta; // region-origin displacement, reserved, successful channel mask
    vec4 workFirstNode;
    ivec4 workDemand; // active direct sources, direction demand, newly active sources, diffuse demand
    uvec4 workFrame; // frame, observation revision, storage generation, reserved
    vec4 workPlanes[6];
};
layout(std430) buffer WorkRegions { uint regions[]; };
layout(std430) buffer WorkScratch { uint scratch[]; };
const uint DIRECT_LIST=64u, DIFFUSE_LIST=576u, HEAP=832u;
const uint VISIBLE=4096u;
/** Converts one region index into its bounded coordinates. */
ivec3 RegionPoint(uint index)
{
    int side=workExtent.z;
    return ivec3(int(index)%side,(int(index)/side)%8,int(index)/(side*8));
}
/** Converts a bounded region coordinate back into its stable lattice ordering. */
uint RegionIndex(ivec3 p) { return uint((p.z*8+p.y)*workExtent.z+p.x); }
/** Excludes unused alignment slots without fabricating work. */
bool ActiveRegion(uint index)
{
    ivec3 p=RegionPoint(index), alignment=ivec3(workAlignment.x,0,workAlignment.y);
    ivec3 lo=max(p*4-alignment,ivec3(0));
    ivec3 hi=min((p+1)*4-alignment,ivec3(workExtent.w,32,workExtent.w));
    return all(lessThan(lo,hi));
}
/** Counts allocated slots, including bounded alignment padding. */
uint RegionCount() { return uint(workExtent.z*8*workExtent.z); }

#if VGE_LIGHTING_WORK == 0
/** Keeps a fair cursor at the same absolute key, or the next surviving key in its old cyclic order. */
uint TranslateCursor(uint cursor,uint channels)
{
    uint total=RegionCount()*channels;
    for(uint i=0u;i<total;i++)
    {
        uint old=(cursor+i)%total;
        ivec3 p=RegionPoint(old/channels)-ivec3(workDelta.x,0,workDelta.y);
        if(all(greaterThanEqual(p,ivec3(0))) && all(lessThan(p,ivec3(workExtent.z,8,workExtent.z))))
        {
            uint region=RegionIndex(p);
            if(ActiveRegion(region)) return region*channels+old%channels;
        }
    }
    return 0u;
}
/** Clears only transient demand/counters; retained dirty ages and cursors survive ordinary publications. */
void main()
{
    uint index=gl_GlobalInvocationID.x;
    if(index<RegionCount()) { /* New-slab demand comes from the preceding remap. */
        regions[index*16u+14u]&=1u;
    }
    if(index==0u)
    {
        if(scratch[0]!=workFrame.z)
        {
            for(uint i=0u;i<64u;i++) scratch[i]=0u;
            scratch[0]=workFrame.z;
        }
        else
        {
            scratch[1]=TranslateCursor(scratch[1],2u);
            scratch[2]=TranslateCursor(scratch[2],1u);
        }
        for(uint i=3u;i<=10u;i++) scratch[i]=0u;
    }
}
#elif VGE_LIGHTING_WORK == 1
@import "./work_observation.glsl"
#elif VGE_LIGHTING_WORK == 2
/** Checks the complete region AABB against the current native-local frustum. */
bool VisibleRegion(uint index)
{
    ivec3 p=RegionPoint(index), alignment=ivec3(workAlignment.x,0,workAlignment.y);
    vec3 lo=workFirstNode.xyz+(vec3(max(p*4-alignment,ivec3(0)))+0.5)*vec3(100,25,100);
    vec3 hi=workFirstNode.xyz+(vec3(min((p+1)*4-alignment,ivec3(workExtent.w,32,workExtent.w)))+0.5)*vec3(100,25,100);
    for(int i=0;i<6;i++)
    {
        vec3 positive=mix(lo,hi,greaterThanEqual(workPlanes[i].xyz,vec3(0)));
        if(dot(workPlanes[i].xyz,positive)+workPlanes[i].w<0.0) return false;
    }
    return true;
}
/** Coalesces demand without resetting pending ages or treating enqueued work as completed. */
void main()
{
    uint index=gl_GlobalInvocationID.x;
    if(index>=RegionCount()) return;
    uint base=index*16u;
    if(!ActiveRegion(index)) { regions[base+1u]=0u; return; }
    uint flags=regions[base+1u]&~(VISIBLE|0x70000u);
    uint demand=regions[base+14u];
    bool hard=(demand&1u)!=0u;
    if(VisibleRegion(index)) flags|=VISIBLE;
    for(uint channel=0u;channel<3u;channel++)
    {
        uint bit=1u<<channel;
        bool enabled=channel==2u?workHistory.w!=0:(uint(workDemand.x)&bit)!=0u;
        if(!enabled) { flags&=~(bit|(bit<<4u)|(bit<<8u)); continue; }
        bool dirty=hard || scratch[3]!=0u || (channel<2u?((uint(workDemand.y)|uint(workDemand.z))&bit)!=0u:
            workDemand.w!=0 || scratch[11]!=0u);
        // Diffuse refinement remains eligible after a completed iteration, without a freshness claim.
        if(channel==2u && (flags&bit)==0u) dirty=true;
        if(dirty)
        {
            if((flags&bit)==0u) regions[base+5u+channel]=workFrame.x;
            flags|=bit;
            regions[base+11u+channel]=workFrame.y;
        }
        if(hard) flags|=bit<<4u;
        if(channel<2u && (uint(workDemand.z)&bit)!=0u) flags|=bit<<8u;
        if((flags&bit)!=0u)
        {
            atomicAdd(scratch[channel==2u?7:6],1u);
            atomicMax(scratch[channel==2u?9:8],workFrame.x-regions[base+2u+channel]);
        }
    }
    regions[base+1u]=flags;
}
#elif VGE_LIGHTING_WORK == 3
uniform sampler2D workMap;
layout(rgba16f) uniform image3D workNodes;
/** Checks current native occupancy independently of retained lighting, including outside the vertical band. */
bool NodeOccupied(vec3 q)
{
    ivec2 cell=ivec2(floor(q.xz/50.0));
    if(any(lessThan(cell,ivec2(0)))||any(greaterThanEqual(cell,ivec2(workExtent.y)))) return false;
    vec4 data=texelFetch(workMap,cell,0);
    vec2 heights=50.0*data.ba;
    return !any(isnan(data))&&!any(isinf(data))&&!any(isinf(heights))&&data.r>0.0&&data.a>data.b&&q.y>=heights.x&&q.y<=heights.y;
}
/** Gives each node one writer; column comparisons never race on alpha or encoded RGB. */
void main()
{
    uint index=gl_GlobalInvocationID.x, size=uint(workExtent.x);
    if(index>=size*34u*size) return;
    ivec3 p=ivec3(int(index%size),int((index/size)%34u),int(index/(size*34u)));
    vec4 node=imageLoad(workNodes,p);
    bool interior=all(greaterThan(p,ivec3(0)))&&all(lessThan(p,ivec3(workExtent.x-1,33,workExtent.x-1)));
    uint mask=0u;
    if(interior && NodeOccupied(workFirstNode.xyz+vec3(p)*vec3(100,25,100)))
    {
        ivec3 region=(p-1+ivec3(workAlignment.x,0,workAlignment.y))/4;
        bool hard=(regions[RegionIndex(region)*16u+14u]&1u)!=0u;
        mask=hard?1u:(uint(node.a)|1u);
        mask&=~(uint(workDemand.z)<<1u);
    }
    node.a=float(mask); imageStore(workNodes,p,node);
}
#elif VGE_LIGHTING_WORK == 4
@import "./work_selection.glsl"
#elif VGE_LIGHTING_WORK == 5
/** Records completion only for successfully submitted selected channels; dependencies remain pending. */
void main()
{
    uint index=gl_GlobalInvocationID.x;
    if(index>=RegionCount()) return;
    uint base=index*16u, flags=regions[base+1u];
    for(uint channel=0u;channel<3u;channel++)
    {
        uint bit=1u<<channel;
        if((uint(workDelta.w)&bit)==0u || (flags&(bit<<16u))==0u) continue;
        if((regions[base+15u]&bit)!=0u) continue;
        regions[base+2u+channel]=workFrame.x;
        regions[base+8u+channel]=regions[base+11u+channel];
        flags&=~(bit|(bit<<4u)|(bit<<8u)|(bit<<16u));
        atomicOr(scratch[11],1u);
    }
    regions[base+1u]=flags;
}
#endif
