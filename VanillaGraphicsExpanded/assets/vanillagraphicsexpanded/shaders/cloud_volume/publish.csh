#version 430 core
layout(local_size_x=8,local_size_y=4,local_size_z=8) in;
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
shared uint blockFlags;

/** Copies only changed complete blocks into a disjoint drawable publication; metadata remains coherent. */
void main()
{
    ivec3 b=ivec3(gl_WorkGroupID),p=b*ivec3(8,4,8)+ivec3(gl_LocalInvocationID);
    int r=(b.z*publishExtent.z+b.y)*publishExtent.y+b.x;
    if(gl_LocalInvocationIndex==0u)blockFlags=regions[r*16];
    barrier();
    uint flags=blockFlags;
    if(gl_LocalInvocationIndex==0u)
    {
        for(int i=0;i<16;i++)publishedRegions[r*16+i]=regions[r*16+i];
        publishedOccupancy[r]=occupancy[r];regions[r*16]=flags&~(4096u|8192u|16384u);
    }
    if(any(greaterThanEqual(p,ivec3(publishExtent.x,66,publishExtent.x))))return;
    if((publishExtent.w&1)!=0||(flags&4096u)!=0u)imageStore(targetDensity,p,texelFetch(sourceDensity,p,0));
    if((publishExtent.w&2)!=0||(flags&8192u)!=0u)imageStore(targetDirect,p,texelFetch(sourceDirect,p,0));
    if((publishExtent.w&4)!=0||(flags&16384u)!=0u)imageStore(targetDiffuse,p,texelFetch(sourceDiffuse,p,0));
}
