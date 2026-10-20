#version 430 core
#if VGE_VOLUME_DENSITY_OPERATION==3
layout(local_size_x=8,local_size_y=4,local_size_z=8) in;
#else
layout(local_size_x=64) in;
#endif
layout(std140) uniform VolumeDensity
{
    ivec4 volumeExtent; // size, height, native width, physical blocks X
    ivec4 volumeWindow; // first slots X/Z, descriptor window slots X/Z
    ivec4 volumeHistory; // previous-origin minus current-origin X/Z, history valid
    vec4 volumeFirst; // first logical guard lower corner in native coordinates
    vec4 volumeCell;
    ivec4 volumeStamps; // frame, snapshot revision, total blocks, initial fill
    ivec4 volumeSettings; // budget, blocks Y, snapshot frame
    vec4 volumeNoiseOrigin;
};
uniform sampler2D nativeMap;
layout(rgba32f) uniform image2D descriptors;
layout(r16f) writeonly uniform image3D densityOutput;
uniform sampler3D storedDensity;
layout(std430) buffer DensityRegions { uint regions[]; };
layout(std430) buffer DensityOccupancy { uint occupancy[]; };
layout(std430) buffer DensityScratch { uint scratch[]; };
layout(std430) buffer DensitySummary { uint summary[]; };
layout(std430) buffer DensityCommands { uint commands[]; };

/** Maps float bits to unsigned ordering for a tiny GPU envelope reduction. */
uint Ordered(float x) { uint b=floatBitsToUint(x);return (b&0x80000000u)!=0u?~b:b^0x80000000u; }
/** Canonicalizes empty support and validates only native extinction/height inputs used by generation. */
vec4 NativeColumn(ivec2 cell)
{
    vec4 v=texelFetch(nativeMap,cell,0);
    if(any(isnan(v.rba))||any(isinf(v.rba))||v.r<0) {atomicOr(summary[0],1u);return vec4(0);}
    if(v.r==0) return vec4(0,0,0,1);
    vec3 physical=vec3(v.r/50.0,v.b*50.0,v.a*50.0);
    if(any(isnan(physical))||any(isinf(physical))||physical.z<=physical.y)
    {atomicOr(summary[0],1u);return vec4(0);}
    return vec4(physical,1);
}
/** Marks a changed column plus connected-face neighbors, coalescing physical block demand atomically. */
void DirtyColumn(ivec2 cell)
{
    ivec2 lo=ivec2(floor((vec2(cell)*50.0-50.0-volumeFirst.xz)/volumeCell.xz))-1;
    ivec2 hi=ivec2(ceil((vec2(cell)*50.0+100.0-volumeFirst.xz)/volumeCell.xz))+1;
    lo=clamp(lo,ivec2(0),ivec2(volumeExtent.x));hi=clamp(hi,ivec2(0),ivec2(volumeExtent.x));
    for(int z=lo.y;z<hi.y;)
    {
        int pz=(z+volumeWindow.y)%volumeExtent.x;
        for(int x=lo.x;x<hi.x;)
        {
            int px=(x+volumeWindow.x)%volumeExtent.x;
            for(int y=0;y<volumeSettings.y;y++)
                atomicOr(regions[((pz/8*volumeSettings.y+y)*volumeExtent.w+px/8)*16],16u);
            x+=min(hi.x-x,min(8-px%8,volumeExtent.x-px));
        }
        z+=min(hi.y-z,min(8-pz%8,volumeExtent.x-pz));
    }
    atomicOr(scratch[6],1u);
}

#if VGE_VOLUME_DENSITY_OPERATION==2
/** Orders oldest pending work first; fair selection below prevents starvation under continuous edits. */
bool Better(uint a,uint b)
{
    uint aa=uint(volumeStamps.x)-regions[a*16u+3u],bb=uint(volumeStamps.x)-regions[b*16u+3u];
    return aa>bb||(aa==bb&&a<b);
}
/** Keeps a bounded worst-first heap rather than scanning the full domain once for each output job. */
void Offer(uint item,inout int count,int limit)
{
    if(count<limit)
    {
        int i=count++;scratch[8192+i]=item;
        while(i>0)
        {
            int p=(i-1)/2;if(!Better(scratch[8192+p],scratch[8192+i]))break;
            uint t=scratch[8192+p];scratch[8192+p]=scratch[8192+i];scratch[8192+i]=t;i=p;
        }
    }
    else if(Better(item,scratch[8192]))
    {
        scratch[8192]=item;int i=0;
        while(i*2+1<count)
        {
            int child=i*2+1;if(child+1<count&&Better(scratch[8192+child],scratch[8192+child+1]))child++;
            if(!Better(scratch[8192+i],scratch[8192+child]))break;
            uint t=scratch[8192+i];scratch[8192+i]=scratch[8192+child];scratch[8192+child]=t;i=child;
        }
    }
}
#endif
#if VGE_VOLUME_DENSITY_OPERATION==3
@import "density_shape.glsl"
#endif

/** Executes one barrier-separated structural operation against candidate-owned storage. */
void main()
{
    uint id=gl_GlobalInvocationID.x;
    #if VGE_VOLUME_DENSITY_OPERATION==0
    if(id>=uint(volumeExtent.z*volumeExtent.z))return;
    vec4 v=NativeColumn(ivec2(int(id)%volumeExtent.z,int(id)/volumeExtent.z));
    if(v.x>0&&v.w>0){atomicMin(summary[1],Ordered(v.y));atomicMax(summary[2],Ordered(v.z));atomicAdd(summary[3],1u);}
    #elif VGE_VOLUME_DENSITY_OPERATION==1
    if(id>=uint(volumeExtent.z*volumeExtent.z))return;
    ivec2 cell=ivec2(int(id)%volumeExtent.z,int(id)/volumeExtent.z),slot=(cell+volumeWindow.zw)%volumeExtent.z;
    vec4 now=NativeColumn(cell),old=imageLoad(descriptors,slot);
    ivec2 prior=cell-volumeHistory.xy;
    bool retained=volumeHistory.z!=0&&all(greaterThanEqual(prior,ivec2(0)))&&all(lessThan(prior,ivec2(volumeExtent.z)));
    if(!retained||any(notEqual(now,old)))DirtyColumn(cell);
    if(volumeHistory.z!=0)
    {
        ivec2 oldSlot=(volumeWindow.zw+volumeHistory.xy%volumeExtent.z+volumeExtent.z)%volumeExtent.z;
        ivec2 oldCell=(slot-oldSlot+volumeExtent.z)%volumeExtent.z;
        ivec2 departed=oldCell+volumeHistory.xy;
        if(any(lessThan(departed,ivec2(0)))||any(greaterThanEqual(departed,ivec2(volumeExtent.z))))DirtyColumn(departed);
    }
    imageStore(descriptors,slot,now);
    #elif VGE_VOLUME_DENSITY_OPERATION==2
    if(id!=0u)return;
    int total=volumeStamps.z,budget=volumeSettings.x,count=0,backlog=0;
    for(int i=0;i<total;i++)
    {
        uint flags=regions[i*16];bool pending=(flags&1u)!=0u;
        if((flags&16u)!=0u|| (pending&&regions[i*16+1]==0u))
        {
            if(!pending||regions[i*16+1]==0u)regions[i*16+3]=uint(volumeStamps.w!=0?volumeSettings.z:volumeStamps.x);
            regions[i*16+1]=uint(volumeStamps.y);flags|=1u;
        }
        flags&=~(16u|256u);regions[i*16]=flags;
        if((flags&1u)!=0u)backlog++;
    }
    int fair=volumeStamps.w!=0?budget:budget/2,cursor=int(scratch[5]%uint(total));
    for(int n=0;n<total&&count<fair;n++)
    {
        int i=(cursor+n)%total;
        if((regions[i*16]&1u)!=0u){scratch[64+count++]=uint(i);regions[i*16]|=256u;scratch[5]=uint((i+1)%total);}
    }
    int heapCount=0,limit=budget-count;
    if(limit>0)for(int i=0;i<total;i++)if((regions[i*16]&(1u|256u))==1u)Offer(uint(i),heapCount,limit);
    for(int i=0;i<heapCount;i++){uint job=scratch[8192+i];scratch[64+count++]=job;regions[job*16u]|=256u;}
    scratch[4]=uint(count);summary[4]=0u;summary[5]=uint(count);summary[6]=uint(backlog);
    commands[0]=uint(count);commands[1]=1u;commands[2]=1u;
    #elif VGE_VOLUME_DENSITY_OPERATION==3
    uint job=gl_WorkGroupID.x;if(job>=scratch[4])return;
    int region=int(scratch[64+job]);
    ivec3 block=ivec3(region%volumeExtent.w,(region/volumeExtent.w)%volumeSettings.y,region/(volumeExtent.w*volumeSettings.y));
    if(gl_LocalInvocationIndex==0u)
    {
        for(int z=-1;z<=1;z++)for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
        {
            ivec3 b=block+ivec3(x,y,z);if(b.y<0||b.y>=volumeSettings.y)continue;
            b.x=(b.x+volumeExtent.w)%volumeExtent.w;b.z=(b.z+volumeExtent.w)%volumeExtent.w;
            atomicOr(occupancy[(b.z*volumeSettings.y+b.y)*volumeExtent.w+b.x],2u);
        }
    }
    ivec3 p=block*ivec3(8,4,8)+ivec3(gl_LocalInvocationID);
    if(any(greaterThanEqual(p,ivec3(volumeExtent.x,volumeExtent.y,volumeExtent.x))))return;
    ivec3 q=ivec3((p.x-volumeWindow.x+volumeExtent.x)%volumeExtent.x,p.y,(p.z-volumeWindow.y+volumeExtent.x)%volumeExtent.x);
    float density=0;
    if(all(greaterThan(q,ivec3(0)))&&all(lessThan(q,ivec3(volumeExtent.x,volumeExtent.y,volumeExtent.x)-1)))
        density=CellDensity(volumeFirst.xyz+vec3(q)*volumeCell.xyz);
    if(isnan(density)||isinf(density)||density<0||density>65504){atomicOr(summary[0],2u);density=0;}
    imageStore(densityOutput,p,vec4(density));
    #elif VGE_VOLUME_DENSITY_OPERATION==4
    if(id>=uint(volumeStamps.z)||(occupancy[id]&2u)==0u)return;
    int region=int(id);ivec3 b=ivec3(region%volumeExtent.w,(region/volumeExtent.w)%volumeSettings.y,region/(volumeExtent.w*volumeSettings.y));
    ivec3 origin=b*ivec3(8,4,8);bool occupied=false;
    // Include one texel around the physical block: hardware trilinear support extends across its boundary.
    for(int z=-1;z<=8&&!occupied;z++)for(int y=-1;y<=4&&!occupied;y++)for(int x=-1;x<=8&&!occupied;x++)
    {
        ivec3 p=origin+ivec3(x,y,z);if(p.y<0||p.y>=volumeExtent.y)continue;
        p.x=(p.x+volumeExtent.x)%volumeExtent.x;p.z=(p.z+volumeExtent.x)%volumeExtent.x;
        occupied=texelFetch(storedDensity,p,0).r>0;
    }
    occupancy[id]=occupied?1u:0u;
    #elif VGE_VOLUME_DENSITY_OPERATION==5
    if(id>=uint(volumeStamps.z)||summary[0]!=0u)return;
    uint base=id*16u,flags=regions[base];
    if((flags&256u)!=0u){flags&=~(1u|256u);regions[base+2u]=uint(volumeStamps.y);regions[base+4u]=uint(volumeStamps.x);}
    if(scratch[4]>0u)
    {
        if((flags&2u)==0u)regions[base+9u]=uint(volumeStamps.x);
        if((flags&4u)==0u)regions[base+14u]=uint(volumeStamps.x);
        // Lighting has its own revision namespace. Queue demand here, never stamp its
        // targets with density revisions that could equal unrelated completed light work.
        flags|=6u;if(id==0u)atomicOr(scratch[6],1u);
    }
    regions[base]=flags;if((flags&1u)!=0u)atomicAdd(summary[4],1u);
    #endif
}
