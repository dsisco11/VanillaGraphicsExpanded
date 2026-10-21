#version 430 core
@import "traversal.glsl"
layout(local_size_x=8,local_size_y=4,local_size_z=8) in;
layout(std140) uniform VolumeDirect
{
    ivec4 directSize;
    ivec4 directSlots;
    vec4 directFirst;
    vec4 directCell;
    vec4 directMinimum;
    vec4 directMaximum;
    vec4 directDirection;
    ivec4 directSettings;
};
layout(rg16f) uniform image3D directOutput;
layout(std430) buffer DirectScratch { uint scratch[]; };
/** Initializes newly relevant channels, or integrates one independently compiled source without RG write races. */
void main()
{
    #if VGE_VOLUME_DIRECT_OPERATION==0
    ivec3 p=ivec3(gl_WorkGroupID)*ivec3(8,4,8)+ivec3(gl_LocalInvocationID);
    if(any(greaterThanEqual(p,directSize.xyz)))return;
    vec4 value=imageLoad(directOutput,p);
    if((directSettings.x&1)!=0)value.r=1;
    if((directSettings.x&2)!=0)value.g=1;
    imageStore(directOutput,p,value);
    #else
    const int source=VGE_VOLUME_DIRECT_OPERATION-1;
    int region=int(scratch[16768+source*256+int(gl_WorkGroupID.x)]);
    ivec3 blocks=ivec3(directSlots.z,directSlots.w,directSlots.z);
    ivec3 block=ivec3(region%blocks.x,(region/blocks.x)%blocks.y,region/(blocks.x*blocks.y));
    ivec3 p=block*ivec3(8,4,8)+ivec3(gl_LocalInvocationID);
    if(any(greaterThanEqual(p,directSize.xyz)))return;
    // Both the physical texel and circular slot are in range, so one negative wrap suffices.
    ivec3 logical=ivec3(p.x-directSlots.x,p.y,p.z-directSlots.y);
    if(logical.x<0)logical.x+=directSize.x;
    if(logical.z<0)logical.z+=directSize.z;
    vec3 position=directFirst.xyz+(vec3(logical)+.5)*directCell.xyz;
    float transmission=1;
    // Empty/guard nodes have no scattering source and need no lighting integral.
    float nodeDensity=texelFetch(volumeDensity,p,0).r;
    if(isnan(nodeDensity)||isinf(nodeDensity)||nodeDensity<0){atomicOr(scratch[25],1u<<uint(source));return;}
    if(nodeDensity>0)
    {
        CloudVolumeGrid grid;grid.size=directSize.xyz;grid.blocks=blocks;grid.slot=directSlots.xy;
        grid.first=directFirst.xyz;grid.cell=directCell.xyz;grid.minimum=directMinimum.xyz;grid.maximum=directMaximum.xyz;
        CloudVolumeRay ray=CloudVolumeBegin(grid,position,directDirection.xyz,3.402823e38);
        float tau=0;
        for(int i=0;i<2048;i++)
        {
            vec2 interval;bool occupied;if(!CloudVolumeNext(grid,ray,interval,occupied))break;
            if(occupied)
            {
                float sigma=texelFetch(volumeDensity,ray.sampleCell,0).r;
                if(isnan(sigma)||isinf(sigma)||sigma<0){ray.failed=true;break;}
                tau=min(80.0,tau+sigma*(interval.y-interval.x));
            }
        }
        if(!CloudVolumeComplete(ray)){atomicOr(scratch[25],1u<<uint(source));return;}
        transmission=exp(-tau);
        if(isnan(transmission)||isinf(transmission)){atomicOr(scratch[25],1u<<uint(source));return;}
    }
    vec4 value=imageLoad(directOutput,p);value[source]=transmission;imageStore(directOutput,p,value);
    #endif
}
