#version 430 core
@import "./includes/cloud_volume_parameters.glsl"
@import "./includes/cloud_volume_density.glsl"
@import "./includes/cloud_volume_primary.glsl"
@import "./includes/cloud_volume_hierarchy.glsl"
@import "./includes/cloud_shadow_frame.glsl"
@import "./includes/cloud_shadow_segments.glsl"
#ifndef VGE_CLOUD_SHADOW_POLICY
#define VGE_CLOUD_SHADOW_POLICY 1
#endif
#if VGE_CLOUD_SHADOW_POLICY > 0
@import "./includes/cloud_shadow_support.glsl"
#endif
layout(local_size_x=8,local_size_y=8) in;
layout(rgba32f) uniform writeonly image2D shadowCacheTarget;
layout(std140) uniform CloudShadowCacheInputs { ivec4 shadowCache; };
/** Retains the original finite midpoint quadrature for comparisons and expensive support-walk fallbacks. */
void CloudShadowUniformIntegrate(vec3 origin,vec3 direction,vec2 span,int count,int segments,
    inout vec4 opticalDepth[4],out float tau)
{
    float ds=(span.y-span.x)/float(count);tau=0.0;
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
        for(int group=0;group<4;group++)
        {
            if(group>=segments/4) break;
            vec4 overlap=CloudShadowSegmentOverlap(vec2(near,far),span,group,segments);
            opticalDepth[group]=min(vec4(1e30),opticalDepth[group]+overlap*sigma*volumeAnchorUnits.z);
        }
        tau=min(1e30,tau+sigma*ds*volumeAnchorUnits.z);
    }
}
/** Integrates a bounded support-aware area quadrature into finite-depth attenuation records. */
void main()
{
    ivec2 pixel=ivec2(gl_GlobalInvocationID.xy);
    if(any(greaterThanEqual(pixel,ivec2(shadowCache.x)))) return;
    int segments=shadowCache.w;
    vec4 opticalDepth[4];
    for(int group=0;group<4;group++) opticalDepth[group]=vec4(0);
    vec3 light=shadowCache.z==0?volumeSun.xyz:volumeMoon.xyz;
    vec3 center,u,v,radius;
    vec4 record=vec4(0);
    if(CloudShadowFrame(shadowCache.z,light,center,u,v,radius))
    {
        record.w=float(shadowCache.z+1);
#if VGE_CLOUD_SHADOW_POLICY == 0
        vec2 uv=(vec2(pixel)+.5)/float(shadowCache.x);
        vec3 origin=center+u*((uv.x*2.0-1.0)*radius.x)+v*((uv.y*2.0-1.0)*radius.y)+light*radius.z;
        vec3 direction=-light;
        vec2 span;
        if(CloudPrimarySpan(origin,direction,2.0*radius.z,span)&&CloudRootSpan(origin,direction,span))
        {
            int count=clamp(shadowCache.y,1,128);
            record.xy=span;
            float tau;
            CloudShadowUniformIntegrate(origin,direction,span,count,segments,opticalDepth,tau);
            record.z=tau;
        }
#else
        int cap=clamp(shadowCache.y,1,128);
        int rays=1;
#if VGE_CLOUD_SHADOW_POLICY == 2
        rays=cap>=2?2:1;
#endif
        vec3 origins[2];vec2 clipped[2],chords[2];float lengths[2];
        // Two opposite quarter-texel points form a bounded area quadrature.
        // Each ray clips independently; no off-center support borrows the center chord.
        for(int ray=0;ray<2;ray++)
        {
            if(ray>=rays) break;
            vec2 offset=rays==1?vec2(0):vec2(ray==0?-.25:.25);
            vec2 uv=(vec2(pixel)+.5+offset)/float(shadowCache.x);
            origins[ray]=center+u*((uv.x*2.0-1.0)*radius.x)+v*((uv.y*2.0-1.0)*radius.y)+light*radius.z;
            lengths[ray]=0.0;clipped[ray]=vec2(0);chords[ray]=vec2(0);
            vec2 span;
            if(CloudShadowDomainSpan(origins[ray],-light,2.0*radius.z,span)&&CloudRootSpan(origins[ray],-light,span))
                clipped[ray]=span;
        }
#if VGE_CLOUD_SHADOW_POLICY == 1
        // Bound support-query overhead before fetching columns. Grazing chords
        // keep the original whole-ray quadrature rather than paying a full grid scan.
        float crossings=1.0+dot(ceil(abs(light.xz)*(clipped[0].y-clipped[0].x)/50.0),vec2(1));
        if(crossings>16.0)
        {
            float tau;record.xy=clipped[0];
            CloudShadowUniformIntegrate(origins[0],-light,clipped[0],cap,segments,opticalDepth,tau);
            record.z=tau;
        }
        else
#endif
        {
            bool valid=true;float totalLength=0.0;
            for(int ray=0;ray<2;ray++)
            {
                if(ray>=rays) break;
                if(clipped[ray].y>clipped[ray].x
                    &&!CloudShadowSupportMeasure(origins[ray],-light,clipped[ray],chords[ray],lengths[ray])) valid=false;
                if(lengths[ray]>0.0)
                {
                    if(totalLength==0.0) record.xy=chords[ray];
                    else record.xy=vec2(min(record.x,chords[ray].x),max(record.y,chords[ray].y));
                    totalLength+=lengths[ray];
                }
            }
            float tau=0.0;
            if(valid) for(int ray=0;ray<2;ray++)
            {
                if(ray>=rays) break;
                if(!CloudShadowSupportIntegrate(origins[ray],-light,clipped[ray],record.xy,lengths[ray],
                    cap/rays,segments,1.0/float(rays),opticalDepth,tau)) valid=false;
            }
            // An incomplete bounded support walk is unavailable, never certified empty.
            record.z=tau;if(!valid) record.w=0.0;
        }
#endif
    }
    imageStore(shadowCacheTarget,pixel,record);
    for(int group=0;group<4;group++)
    {
        if(group>=segments/4) break;
        imageStore(shadowCacheTarget,pixel+ivec2(0,(group+1)*shadowCache.x),opticalDepth[group]);
    }
}
