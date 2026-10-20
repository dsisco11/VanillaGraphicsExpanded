#version 430 core
@import "traversal.glsl"
@import "encoding.glsl"
layout(local_size_x=8,local_size_y=4,local_size_z=8) in;
layout(std140) uniform VolumeDiffuse
{
    ivec4 diffuseSize;
    ivec4 diffuseSlots;
    vec4 diffuseFirst;
    vec4 diffuseCell;
    vec4 diffuseMinimum;
    vec4 diffuseMaximum;
    vec4 diffuseSun;
    vec4 diffuseMoon;
    vec4 diffuseSolar;
    vec4 diffuseLunar;
    vec4 diffuseEnvironment;
    vec4 diffuseCap;
    vec4 diffuseScales;
};
uniform sampler3D diffuseRead;
#if VGE_VOLUME_DIFFUSE_SHADOWS
uniform sampler3D directRead;
#endif
layout(rgba16f) writeonly uniform image3D diffuseWrite;
layout(std430) buffer DiffuseScratch { uint scratch[]; };
/** Tests physical RGB before arithmetic and publication. */
bool VolumeDiffuseFinite(vec3 value){return !any(isnan(value))&&!any(isinf(value))&&all(greaterThanEqual(value,vec3(0)));}
/** Evaluates the declared unit-integral HG response for one fixed gather direction. */
float VolumeDiffusePhase(float cosine)
{
    float denominator=1.36-1.2*clamp(cosine,-1.0,1.0);
    return .64/(12.566370614359172*denominator*sqrt(denominator));
}
/** Gathers first scattering plus the coherent old isotropic source, then closes the ray once with environment. */
bool VolumeDiffuseGather(CloudVolumeGrid grid,vec3 origin,vec3 direction,out vec3 incoming)
{
    CloudVolumeRay ray=CloudVolumeBegin(grid,origin,direction,400,.5);
    vec3 solar=.9*diffuseSolar.rgb*VolumeDiffusePhase(dot(-direction,diffuseSun.xyz));
    vec3 lunar=.9*diffuseLunar.rgb*VolumeDiffusePhase(dot(-direction,diffuseMoon.xyz));
    float transmission=1;incoming=vec3(0);
    for(int i=0;i<2048;i++)
    {
        vec2 interval;bool occupied;if(!CloudVolumeNext(grid,ray,interval,occupied))break;
        if(!occupied)continue; // Empty gaps do not attenuate or terminate transport.
        vec3 position=origin+direction*((interval.x+interval.y)*.5);
        vec3 uv=((position-grid.first)/grid.cell+vec3(grid.slot.x,0,grid.slot.y))/vec3(grid.size);
        float sigma=textureLod(volumeDensity,uv,0).r;
        if(isnan(sigma)||isinf(sigma)||sigma<0)return false;
        if(sigma==0)continue;
        vec2 visibility=vec2(1);
        #if VGE_VOLUME_DIFFUSE_SHADOWS
        visibility=textureLod(directRead,uv,0).rg;
        if(any(isnan(visibility))||any(isinf(visibility))||any(lessThan(visibility,vec2(0)))||any(greaterThan(visibility,vec2(1))))return false;
        #endif
        vec3 oldSource=textureLod(diffuseRead,uv,0).rgb*diffuseScales.x;
        if(!VolumeDiffuseFinite(oldSource))return false;
        float stepTransmission=exp(-sigma*(interval.y-interval.x));
        incoming+=transmission*(1-stepTransmission)*(solar*visibility.x+lunar*visibility.y+oldSource);
        transmission*=stepTransmission;
    }
    if(!CloudVolumeComplete(ray))return false;
    incoming+=transmission*diffuseEnvironment.rgb;
    return VolumeDiffuseFinite(incoming);
}
/** Refines only selected physical blocks while leaving the copied destination elsewhere untouched. */
void main()
{
    int region=int(scratch[16448+int(gl_WorkGroupID.x)]);
    ivec3 blocks=ivec3(diffuseSlots.z,diffuseSlots.w,diffuseSlots.z);
    ivec3 block=ivec3(region%blocks.x,(region/blocks.x)%blocks.y,region/(blocks.x*blocks.y));
    ivec3 p=block*ivec3(8,4,8)+ivec3(gl_LocalInvocationID);
    if(any(greaterThanEqual(p,diffuseSize.xyz)))return;
    ivec3 logical=ivec3((p.x-diffuseSlots.x+diffuseSize.x)%diffuseSize.x,p.y,
        (p.z-diffuseSlots.y+diffuseSize.z)%diffuseSize.z);
    vec3 source=.9*diffuseEnvironment.rgb;
    float density=texelFetch(volumeDensity,p,0).r;
    if(isnan(density)||isinf(density)||density<0){atomicOr(scratch[25],4u);return;}
    if(density>0&&all(greaterThan(logical,ivec3(0)))&&all(lessThan(logical,diffuseSize.xyz-1)))
    {
        CloudVolumeGrid grid;grid.size=diffuseSize.xyz;grid.blocks=blocks;grid.slot=diffuseSlots.xy;
        grid.first=diffuseFirst.xyz;grid.cell=diffuseCell.xyz;grid.minimum=diffuseMinimum.xyz;grid.maximum=diffuseMaximum.xyz;
        vec3 origin=grid.first+(vec3(logical)+.5)*grid.cell;source=vec3(0);
        for(int i=0;i<8;i++)
        {
            vec3 direction=vec3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)*.5773502691896258;
            vec3 incoming;if(!VolumeDiffuseGather(grid,origin,direction,incoming)){atomicOr(scratch[25],4u);return;}
            source+=incoming*(.9/8.0);
        }
    }
    if(!VolumeDiffuseFinite(source)){atomicOr(scratch[25],4u);return;}
    imageStore(diffuseWrite,p,CloudVolumeEncode(source,diffuseCap.rgb,diffuseScales.y));
}
