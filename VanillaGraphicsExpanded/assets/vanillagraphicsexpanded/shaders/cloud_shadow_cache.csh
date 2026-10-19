#version 430 core
@import "./includes/cloud_volume_parameters.glsl"
@import "./includes/cloud_volume_density.glsl"
@import "./includes/cloud_volume_primary.glsl"
@import "./includes/cloud_volume_hierarchy.glsl"
@import "./includes/cloud_shadow_frame.glsl"
layout(local_size_x=8,local_size_y=8) in;
layout(rgba32f) uniform writeonly image2D shadowCacheTarget;
layout(std140) uniform CloudShadowCacheInputs { ivec4 shadowCache; };
/** Generates bounded nearest-filtered front/back/optical-depth moments for one admitted source. */
void main()
{
    ivec2 pixel=ivec2(gl_GlobalInvocationID.xy);
    if(any(greaterThanEqual(pixel,imageSize(shadowCacheTarget)))) return;
    vec3 light=shadowCache.z==0?volumeSun.xyz:volumeMoon.xyz;
    vec3 center,u,v,radius;
    vec4 record=vec4(0);
    if(CloudShadowFrame(light,center,u,v,radius))
    {
        record.w=float(shadowCache.z+1);
        vec2 uv=(vec2(pixel)+.5)/vec2(imageSize(shadowCacheTarget));
        vec3 origin=center+u*((uv.x*2.0-1.0)*radius.x)+v*((uv.y*2.0-1.0)*radius.y)+light*radius.z;
        vec3 direction=-light;
        vec2 span;
        if(CloudPrimarySpan(origin,direction,2.0*radius.z,span)&&CloudRootSpan(origin,direction,span))
        {
            int count=clamp(shadowCache.y,1,128);
            float ds=(span.y-span.x)/float(count),first=0.0,last=0.0,tau=0.0;
            CloudDensityCache cache=CloudDensityCacheStart();
            for(int i=0;i<128;i++)
            {
                if(i>=count) break;
                float near=span.x+float(i)*ds,far=near+ds;
                vec3 point=origin+direction*((near+far)*.5);
                ivec2 cell=ivec2(floor(point.xz/50.0));
                vec4 data=CloudDensityColumn(cell,cache);
                float sigma=CloudDensity(point,cell,data,cache);
                if(sigma<=0.0) continue;
                if(last==0.0) first=near;
                last=far;tau=min(1e30,tau+sigma*ds*volumeAnchorUnits.z);
            }
            if(last>first) record=vec4(first,last,tau,float(shadowCache.z+1));
        }
    }
    imageStore(shadowCacheTarget,pixel,record);
}
