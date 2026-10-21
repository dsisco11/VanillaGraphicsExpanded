#version 430 core
@import "../includes/vge_frame_ubo.glsl"
@import "../includes/pbr_frame_lighting.glsl"
@import "../includes/cloud_temporal_parameters.glsl"
@import "../includes/cloud_reprojection.glsl"
@import "../includes/blue_noise.glsl"
@import "../includes/interleaved_sampling.glsl"
uniform sampler2D temporalBlueNoise;
uniform sampler2D reconstructCloud;
uniform sampler2D reconstructBins;
uniform sampler2D reconstructMetadata;
uniform sampler2D reconstructMaterialDepth;
uniform sampler2D reconstructVisibilityDepth;
uniform sampler2D reconstructLiquidDepth;
uniform sampler2D previousCloud;
uniform sampler2D previousBins;
uniform sampler2D previousMetadata;
layout(location=0) out vec4 outCloud;
layout(location=1) out vec3 outBins;
layout(location=2) out vec4 outMetadata;
/** Rejects failed prefixes and malformed physical samples before they influence history. */
bool StoredTemporalFinite(vec4 cloud,vec3 bins,vec4 meta)
{
    return meta.y>=1&&meta.y<=2&&!any(isnan(meta))&&!any(isinf(meta))
        &&!any(isnan(cloud))&&!any(isinf(cloud))&&all(greaterThanEqual(cloud,vec4(0)))&&cloud.a<=1
        &&!any(isnan(bins))&&!any(isinf(bins))&&all(greaterThanEqual(bins,vec3(0)))&&all(lessThanEqual(bins,vec3(1)));
}
/** Keeps the same conservative full-pixel footprint as the configured reduced trace. */
float StoredTemporalDepth(vec2 uv)
{
    vec4 depth=vec4(1);int divisor=int(cloudLayout.w);
    for(int y=0;y<divisor/2;y++)for(int x=0;x<divisor/2;x++)
    {
        vec2 tap=uv+(vec2(x,y)*2-float(divisor/2-1))/vgeFrame.screenSize;
        depth=min(depth,min(textureGather(reconstructMaterialDepth,tap,0),
            min(textureGather(reconstructVisibilityDepth,tap,0),textureGather(reconstructLiquidDepth,tap,0))));
    }
    return min(min(depth.x,depth.y),min(depth.z,depth.w));
}
/** Reuses compatible physical sampling history, with current spatial observations bounding stale light/shape. */
void main()
{
    outCloud=vec4(0);outBins=vec3(1);outMetadata=vec4(0);
    if(!VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration()))return;
    ivec2 pixel=ivec2(gl_FragCoord.xy),size=ivec2(cloudTrace.xy);
    vec2 uv=(vec2(pixel)+.5)/vec2(size);float depth=StoredTemporalDepth(uv);
    vec4 data=vec4(0),meta=vec4(0),low=vec4(3.402823e38),high=vec4(0);
    vec3 bins=vec3(1),lowBins=vec3(1),highBins=vec3(0);
    float best=3.402823e38;
    // One point in every 2x2 tile is current. A bounded stencil supplies disocclusion fallback;
    // it never launches a second ray or samples unfinished solver storage.
    for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
    {
        ivec2 p=pixel+ivec2(x,y);if(any(lessThan(p,ivec2(0)))||any(greaterThanEqual(p,size)))continue;
        ivec2 source=p;
        if(cloudHistory.x>.5)
        {
            source=p/2;
            float rank=BlueNoiseRank(temporalBlueNoise,source,0u,8);
            if(any(notEqual(p,InterleavedSampleLocation2x2(rank,source,vgeFrame.frameIndex,size))))continue;
        }
        vec4 m=texelFetch(reconstructMetadata,source,0),c=texelFetch(reconstructCloud,source,0);
        vec3 b=texelFetch(reconstructBins,source,0).rgb;
        if(!StoredTemporalFinite(c,b,m)||abs(m.x-depth)>2e-5)continue;
        low=min(low,c);high=max(high,c);lowBins=min(lowBins,b);highBins=max(highBins,b);
        float score=float(x*x+y*y);
        if(score<best){best=score;data=c;bins=b;meta=m;}
    }
    if(best==3.402823e38)return; // No current compatible observation must not resurrect old clouds.
    meta.x=depth;float count=1;
    if(cloudHistory.x>.5&&meta.y==1&&meta.z>0)
    {
        vec3 point=normalize(CloudTemporalPoint(uv,1))*meta.z+cloudPreviousTranslation.xyz;
        vec4 clip=cloudPreviousProjection*vec4(point,1);
        if(clip.w>0)
        {
            vec2 oldUv=clip.xy/clip.w*.5+.5;
            if(all(greaterThanEqual(oldUv,vec2(0)))&&all(lessThan(oldUv,vec2(1))))
            {
                ivec2 base=ivec2(floor(oldUv*vec2(size)-.5));bool accepted=true;float age=8;
                // Continuous history uses two hardware-filtered reads, admitted by exact metadata.
                // Never manually bilinear-filter radiance or mix incompatible boundary samples.
                for(int y=0;y<2;y++)for(int x=0;x<2;x++)
                {
                    vec4 old=texelFetch(previousMetadata,clamp(base+ivec2(x,y),ivec2(0),size-1),0);
                    accepted=accepted&&old.y==1&&old.w>=1&&abs(old.x-depth)<=2e-5
                        &&abs(old.z-length(point))<=max(2.0,length(point)*.05);
                    age=min(age,old.w);
                }
                if(accepted)
                {
                    vec4 old=textureLod(previousCloud,oldUv,0);vec3 oldBins=textureLod(previousBins,oldUv,0).rgb;
                    if(StoredTemporalFinite(old,oldBins,vec4(depth,1,meta.z,age)))
                    {
                        count=min(age+1,8);float weight=(count-1)/count;
                        // Coverage changes react immediately; current-neighborhood bounds also
                        // contain old physical energy when atmosphere or cached lighting changes.
                        weight*=clamp(1-abs(old.a-data.a)*4,0,1);
                        data=mix(data,clamp(old,low,high),weight);
                        bins=mix(bins,clamp(oldBins,lowBins,highBins),weight);
                    }
                }
            }
        }
    }
    outCloud=data;outBins=bins;outMetadata=vec4(depth,meta.y,meta.z,count);
}
