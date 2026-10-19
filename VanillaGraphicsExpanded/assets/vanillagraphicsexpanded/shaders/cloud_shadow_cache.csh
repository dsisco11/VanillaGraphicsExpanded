#version 430 core
@import "./includes/cloud_volume_parameters.glsl"
@import "./includes/cloud_volume_density.glsl"
@import "./includes/cloud_volume_primary.glsl"
@import "./includes/cloud_volume_hierarchy.glsl"
@import "./includes/cloud_shadow_frame.glsl"
#ifndef VGE_CLOUD_SHADOW_POLICY
#define VGE_CLOUD_SHADOW_POLICY 1
#endif
#if VGE_CLOUD_SHADOW_POLICY > 0
@import "./includes/cloud_shadow_support.glsl"
#endif
layout(local_size_x=8,local_size_y=8) in;
layout(rgba32f) uniform writeonly image2D shadowCacheTarget;
layout(std140) uniform CloudShadowCacheInputs { ivec4 shadowCache; };
/** Integrates the original bounded midpoint lattice over one complete clipped chord. */
float CloudShadowUniformIntegrate(vec3 origin,vec3 direction,vec2 span,int count)
{
    float ds=(span.y-span.x)/float(count),tau=0.0;
    CloudDensityCache cache=CloudDensityCacheStart();
    for(int i=0;i<128;i++)
    {
        if(i>=count) break;
        vec3 point=origin+direction*(span.x+(float(i)+.5)*ds);
        ivec2 cell=ivec2(floor(point.xz/50.0));
        vec4 data=CloudDensityColumn(cell,cache);
        float sigma=CloudDensity(point,cell,data,cache);
        tau=min(1e30,tau+sigma*ds*volumeAnchorUnits.z);
    }
    return tau;
}
/** Publishes a compact mean-extinction ramp; empty projected rays have an all-zero record. */
void main()
{
    ivec2 pixel=ivec2(gl_GlobalInvocationID.xy);
    if(any(greaterThanEqual(pixel,ivec2(shadowCache.x)))) return;
    vec3 light=shadowCache.z==0?volumeSun.xyz:volumeMoon.xyz;
    vec3 center,u,v,radius;
    vec4 record=vec4(0);
    if(CloudShadowFrame(shadowCache.z,light,center,u,v,radius))
    {
        int cap=clamp(shadowCache.y,1,128),rays=1;
#if VGE_CLOUD_SHADOW_POLICY == 2
        rays=cap>=2?2:1;
#endif
        vec2 chord=vec2(0);float tau=0.0;
        for(int ray=0;ray<2;ray++)
        {
            if(ray>=rays) break;
            vec2 offset=rays==1?vec2(0):vec2(ray==0?-.25:.25);
            vec2 uv=(vec2(pixel)+.5+offset)/float(shadowCache.x);
            vec3 origin=center+u*((uv.x*2.0-1.0)*radius.x)+v*((uv.y*2.0-1.0)*radius.y)+light*radius.z;
            vec2 span;
#if VGE_CLOUD_SHADOW_POLICY == 0
            bool supported=CloudPrimarySpan(origin,-light,2.0*radius.z,span);
#else
            bool supported=CloudShadowDomainSpan(origin,-light,2.0*radius.z,span);
#endif
            if(!supported||!CloudRootSpan(origin,-light,span)) continue;
            vec2 rayChord=span;float rayTau=0.0;
#if VGE_CLOUD_SHADOW_POLICY > 0
            float crossings=1.0+dot(ceil(abs(light.xz)*(span.y-span.x)/50.0),vec2(1));
            float length=0.0;
            bool complete=false;
            if(crossings<=16.0&&CloudShadowSupportMeasure(origin,-light,span,rayChord,length))
                complete=CloudShadowSupportIntegrate(origin,-light,span,length,cap/rays,1.0,rayTau);
            // Incomplete support never becomes a sentinel in an otherwise filterable texture.
            // Support schedules are validated before any density work, preserving the ray cap.
            if(!complete)
#endif
            {rayChord=span;rayTau=CloudShadowUniformIntegrate(origin,-light,span,cap/rays);}
            if(rayChord.y>rayChord.x)
            {
                chord=chord.y>chord.x?vec2(min(chord.x,rayChord.x),max(chord.y,rayChord.y)):rayChord;
                tau=min(1e30,tau+rayTau/float(rays));
            }
        }
        float metres=(chord.y-chord.x)*volumeAnchorUnits.z;
        if(metres>0.0) record=vec4(chord.x,tau/metres,tau,0);
    }
    // Source identity belongs to the admitted whole-cache binding, never interpolated alpha.
    imageStore(shadowCacheTarget,pixel,record);
}
