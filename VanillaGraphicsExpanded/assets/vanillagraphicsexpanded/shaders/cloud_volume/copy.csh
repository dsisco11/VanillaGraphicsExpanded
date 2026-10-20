#version 430 core
@import "encoding.glsl"
layout(local_size_x=8,local_size_y=4,local_size_z=8) in;
layout(std140) uniform VolumeCopy { ivec4 copyExtent; vec4 copyScales; vec4 copyCap; };
uniform sampler3D oldDiffuse;
layout(rgba16f) writeonly uniform image3D newDiffuse;
layout(std430) buffer CopyScratch { uint scratch[]; };
/** Repairs complete physical blocks, or re-encodes the full field, without changing the gather source. */
void main()
{
    ivec3 block=ivec3(gl_WorkGroupID);
    if(copyExtent.w!=0)
    {
        int region=int(scratch[16640+int(gl_WorkGroupID.x)]);
        int nx=(copyExtent.x+7)/8,ny=(copyExtent.y+3)/4;
        block=ivec3(region%nx,(region/nx)%ny,region/(nx*ny));
    }
    ivec3 origin=block*ivec3(8,4,8),p=origin+ivec3(gl_LocalInvocationID);
    if(gl_LocalInvocationIndex==0u)
    {
        ivec3 size=min(ivec3(8,4,8),copyExtent.xyz-origin);
        if(copyExtent.w!=0)atomicAdd(scratch[39],uint(size.x*size.y*size.z));
        else if(all(equal(block,ivec3(0))))scratch[39]=uint(copyExtent.x*copyExtent.y*copyExtent.z);
    }
    if(any(greaterThanEqual(p,copyExtent.xyz))) return;
    vec4 value=texelFetch(oldDiffuse,p,0);
    // Same-scale repair preserves stored half values exactly, including subnormals.
    imageStore(newDiffuse,p,copyScales.x==copyScales.y?value:
        CloudVolumeEncode(value.rgb*copyScales.x,copyCap.rgb,copyScales.y));
}
