#version 430 core
layout(local_size_x=256) in;
layout(std140) uniform VolumePublish { ivec4 publishExtent; }; // size, blocks X, blocks Y, full channel mask
uniform sampler3D sourceDensity;
uniform sampler3D sourceDirect;
uniform sampler3D sourceDiffuse;
layout(r16f) writeonly uniform image3D targetDensity;
layout(rg16f) writeonly uniform image3D targetDirect;
layout(rgba16f) writeonly uniform image3D targetDiffuse;
layout(std430) buffer PublishRegions { uint regions[]; };
layout(std430) readonly buffer PublishOccupancy { uint occupancy[]; };
layout(std430) writeonly buffer PublishedRegions { uint publishedRegions[]; };
layout(std430) writeonly buffer PublishedOccupancy { uint publishedOccupancy[]; };
layout(std430) buffer PublishScratch { uint scratch[]; };
layout(std430) buffer PublishCommands { uint commands[]; };
const uint List=32768u,Dirty=4096u|8192u|16384u;
shared uint blockFlags;

/** Publishes coherent metadata separately from bounded dirty images, retaining full-channel exceptions. */
void main()
{
    uint blocks=uint(publishExtent.y*publishExtent.z*publishExtent.y);
    #if VGE_VOLUME_PUBLISH_OPERATION==0
    if(gl_GlobalInvocationID.x!=0u)return;
    commands[0]=0u;commands[1]=1u;commands[2]=1u;commands[3]=0u;
    #elif VGE_VOLUME_PUBLISH_OPERATION==1
    uint r=gl_GlobalInvocationID.x;if(r>=blocks)return;
    uint flags=regions[r*16u];
    for(uint i=0u;i<16u;i++)publishedRegions[r*16u+i]=regions[r*16u+i];
    publishedOccupancy[r]=occupancy[r];
    // One producer per physical region bounds the unique list by the block count.
    // Required full-channel copies already visit every block and need no compaction.
    if(publishExtent.w==0&&(flags&Dirty)!=0u)
    {
        uint index=atomicAdd(commands[3],1u);scratch[List+index]=r;
        // Bound X to 64 even when all 71825 blocks are dirty; last-row padding is harmless.
        atomicMax(commands[0],min(index+1u,64u));atomicMax(commands[1],index/64u+1u);
    }
    #else
    uint r;
    if(publishExtent.w!=0)
        r=(gl_WorkGroupID.z*uint(publishExtent.z)+gl_WorkGroupID.y)*uint(publishExtent.y)+gl_WorkGroupID.x;
    else
    {
        uint index=gl_WorkGroupID.y*64u+gl_WorkGroupID.x;
        if(index>=commands[3])return; // Uniform group exit precedes both shared barriers.
        r=scratch[List+index];
    }
    if(gl_LocalInvocationIndex==0u)blockFlags=regions[r*16u];
    barrier();
    uint flags=blockFlags,lane=gl_LocalInvocationIndex;
    ivec3 b=ivec3(int(r)%publishExtent.y,(int(r)/publishExtent.y)%publishExtent.z,
        int(r)/(publishExtent.y*publishExtent.z));
    ivec3 p=b*ivec3(8,4,8)+ivec3(int(lane)%8,(int(lane)/8)%4,int(lane)/32);
    // Partial edge lanes still reach completion before dirty flags are consumed.
    if(all(lessThan(p,ivec3(publishExtent.x,66,publishExtent.x))))
    {
        if((publishExtent.w&1)!=0||(flags&4096u)!=0u)imageStore(targetDensity,p,texelFetch(sourceDensity,p,0));
        if((publishExtent.w&2)!=0||(flags&8192u)!=0u)imageStore(targetDirect,p,texelFetch(sourceDirect,p,0));
        if((publishExtent.w&4)!=0||(flags&16384u)!=0u)imageStore(targetDiffuse,p,texelFetch(sourceDiffuse,p,0));
    }
    barrier();
    if(gl_LocalInvocationIndex==0u)regions[r*16u]=flags&~Dirty;
    #endif
}
