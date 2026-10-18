#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/vge_frame_ubo.glsl"
@import "./includes/cloud_temporal_parameters.glsl"
@import "./includes/cloud_history.glsl"
@import "./includes/cloud_depth.glsl"
@import "./includes/cloud_reconstruction_support.glsl"
layout(location=0) out vec4 outCloudRadianceTransmission;
layout(location=1) out vec4 outCloudBucketTransmission;
layout(location=2) out vec4 outCloudDepth;
uniform sampler2DArray currentPayload;
/** Fetches radiance, bucket transmissions or depth from the disjoint current trace block. */
vec4 CloudCurrentFetch(int layer,ivec2 pixel)
{
    return texelFetch(currentPayload,ivec3(pixel,layer),0);
}

/** Reconstructs camera-relative positions from this view's inverse transforms. */
vec3 CloudTemporalPoint(vec2 uv,float depth)
{
    vec4 p=vgeFrame.invProjectionMatrix*vec4(uv*2.0-1.0,depth*2.0-1.0,1);
    return (vgeFrame.invViewMatrix*vec4(p.xyz/p.w,0)).xyz;
}
/** Allows quantized support endpoints while retaining independent foreground and displacement rejection. */
bool CloudHistoryCompatible(vec4 a,vec4 b,float expected,float currentSpacing,float previousSpacing)
{
    if(b.y<=0.0) return false;
    if(any(isnan(a))||any(isinf(a))||any(isnan(b))||any(isinf(b))) return false;
    vec2 spacing=vec2(currentSpacing,previousSpacing);
    if(any(isnan(spacing))||any(isinf(spacing))||any(lessThan(spacing,vec2(0)))) return false;
    if(max(abs(a.z-b.z),abs(a.w-b.w))>2e-5) return false;
    // Each first/last contributing endpoint has one sample interval of uncertainty. Account
    // for both observations rather than interpreting a jittered boundary as scene motion.
    float uncertainty=currentSpacing+previousSpacing;
    if(abs((b.x+b.y)*.5-expected)>max(max(2.0,expected*.02),uncertainty)) return false;
    return abs((b.y-b.x)-(a.y-a.x))<=max(max(2.0,(a.y-a.x)*.05),2.0*uncertainty);
}
/** Filters physical radiance and attenuation factors together; depth remains the current trace receipt. */
void CloudTemporalHalf()
{
    ivec2 pixel=ivec2(gl_FragCoord.xy), size=ivec2(cloudTrace.xy);
    vec4 data[3]; for(int i=0;i<3;i++) data[i]=CloudCurrentFetch(i,pixel);
    vec4 meta=data[2]; float spacing=data[1].w; bool accepted=false; ivec2 previous=ivec2(0);
    if(cloudHistory.x>.5 && meta.y>0.0 && data[0].a<1.0)
    {
        vec2 uv=CloudTraceUv(pixel,cloudTrace.zw);
        vec3 point=normalize(CloudTemporalPoint(uv,1.0))*((meta.x+meta.y)*.5)+cloudPreviousTranslation.xyz;
        vec4 clip=cloudPreviousProjection*vec4(point,1);
        if(clip.w>0.0)
        {
            vec2 oldUv=clip.xy/clip.w*.5+.5;
            if(all(greaterThanEqual(oldUv,vec2(0)))&&all(lessThan(oldUv,vec2(1))))
            {
                previous=clamp(ivec2(floor((oldUv*vgeFrame.screenSize-cloudHistory.yz-vec2(.5))*.5+vec2(.5))),ivec2(0),size-1);
                // Previous phase is retained explicitly: reject a projection farther than one reduced pixel.
                vec2 sampleUv=CloudTraceUv(previous,cloudHistory.yz);
                if(length((sampleUv-oldUv)*vgeFrame.screenSize)<=2.83)
                    accepted=CloudHistoryCompatible(meta,CloudHistoryFetch(2,previous),length(point),spacing,CloudHistoryFetch(1,previous).w);
            }
        }
    }
    if(accepted)
    {
        // Clamp every previous channel to the compatible current neighborhood. This bounds stale
        // lighting/weather response and preserves the current frame's admitted radiance envelope.
        vec4 low[2],high[2],old[2]; bool finite=true;
        for(int i=0;i<2;i++)
        {
            low[i]=data[i]; high[i]=data[i]; old[i]=CloudHistoryFetch(i,previous);
            if(any(isnan(old[i]))||any(isinf(old[i]))||any(lessThan(old[i],vec4(0)))) finite=false;
        }
        for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++)
        {
            ivec2 n=clamp(pixel+ivec2(x,y),ivec2(0),size-1);
            vec4 m=CloudCurrentFetch(2,n);
            vec4 buckets=CloudCurrentFetch(1,n);
            if(!CloudHistoryCompatible(meta,m,(meta.x+meta.y)*.5,spacing,buckets.w)) continue;
            vec4 radiance=CloudCurrentFetch(0,n);
            low[0]=min(low[0],radiance);high[0]=max(high[0],radiance);
            low[1]=min(low[1],buckets);high[1]=max(high[1],buckets);
        }
        if(finite) for(int i=0;i<2;i++) data[i]=mix(data[i],clamp(old[i],low[i],high[i]),cloudHistory.w);
    }
    outCloudRadianceTransmission=vec4(max(data[0].rgb,vec3(0)),clamp(data[0].a,0.0,1.0));
    // Spacing describes this frame's support receipt, never an average of prior lattices.
    outCloudBucketTransmission=vec4(clamp(data[1].xyz,0.0,1.0),spacing);
    outCloudDepth=meta;
}

/** Rejects invalid physical factors before sharing any interpolation weight across payload channels. */
bool CloudQuarterFinite(vec4 value)
{
    return !any(isnan(value))&&!any(isinf(value))&&!any(lessThan(value,vec4(0)));
}
/** Converts support distances between neighboring rays while retaining the same camera-axis interval. */
float CloudQuarterRayCosine(vec2 uv)
{
    vec4 direction=vgeFrame.invProjectionMatrix*vec4(uv*2.0-1.0,1,1);
    return abs(direction.z)/length(direction.xyz);
}
/** Reconstructs the unsampled half-grid from compatible history/current support without evaluating cloud transport. */
void CloudTemporalQuarter()
{
    ivec2 pixel=ivec2(gl_FragCoord.xy),size=ivec2(cloudTrace.xy),traceSize=ivec2(cloudLayout.xy);
    vec2 uv=CloudReconstructionUv(pixel,vec2(0));
    vec2 normalizedDepth=vec2(CloudOpaqueDepth(uv),textureLod(liquidDepth,uv,0.0).r);
    vec2 depth=vec2(CloudReconstructionDepth(uv,normalizedDepth.x),CloudReconstructionDepth(uv,normalizedDepth.y));
    float limit=min(depth.x,depth.y),destinationCosine=CloudQuarterRayCosine(uv);
    ivec2 center=pixel/2;
    bool sampled=all(equal(CloudTraceLocation(center),pixel));
    vec4 data[2],low[2],high[2];
    for(int i=0;i<2;i++){data[i]=vec4(0);low[i]=vec4(1e30);high[i]=vec4(-1e30);}
    float total=0.0,first=1e30,last=0.0,spacing=0.0;
    for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++)
    {
        ivec2 q=center+ivec2(x,y);
        if(any(lessThan(q,ivec2(0)))||any(greaterThanEqual(q,traceSize)))continue;
        ivec2 location=CloudTraceLocation(q);
        vec2 sourceUv=CloudReconstructionUv(location,vec2(0));
        vec4 meta=CloudCurrentFetch(2,q),optical0=CloudCurrentFetch(0,q),optical1=CloudCurrentFetch(1,q);
        if(!CloudQuarterFinite(optical0)||!CloudQuarterFinite(optical1))continue;
        float compatibility=CloudReconstructionWeight(meta,sourceUv,depth,limit);
        if(compatibility<=0.0)continue;
        vec2 delta=vec2(location-pixel);
        float weight=compatibility/(1.0+dot(delta,delta));
        low[0]=min(low[0],optical0);high[0]=max(high[0],optical0);
        low[1]=min(low[1],optical1);high[1]=max(high[1],optical1);
        if(sampled&&(x!=0||y!=0))continue;
        data[0]+=optical0*weight;data[1]+=optical1*weight;total+=weight;
        if(meta.y>0.0)
        {
            float ratio=CloudQuarterRayCosine(sourceUv)/destinationCosine;
            first=min(first,meta.x*ratio);last=max(last,meta.y*ratio);
            // Unsampled positions retain the widest contributing interval in destination-ray units.
            spacing=max(spacing,optical1.w*ratio);
        }
    }
    vec4 meta=vec4(0,0,normalizedDepth);
    if(total>0.0)
    {
        for(int i=0;i<2;i++)data[i]/=total;
        if(last>0.0)meta.xy=clamp(vec2(first,last),vec2(0),vec2(limit/destinationCosine));
    }
    else
    {
        // Current foreground has no compatible observation: do not resurrect stale cloud history.
        data[0]=vec4(0,0,0,1);data[1]=vec4(1,1,1,0);
    }
    bool accepted=false;ivec2 previous=ivec2(0);
    if(total>0.0&&cloudHistory.x>.5&&meta.y>0.0&&data[0].a<1.0)
    {
        vec3 point=normalize(CloudTemporalPoint(uv,1.0))*((meta.x+meta.y)*.5)+cloudPreviousTranslation.xyz;
        vec4 clip=cloudPreviousProjection*vec4(point,1);
        if(clip.w>0.0)
        {
            vec2 oldUv=clip.xy/clip.w*.5+.5;
            if(all(greaterThanEqual(oldUv,vec2(0)))&&all(lessThan(oldUv,vec2(1))))
            {
                previous=clamp(ivec2(floor((oldUv*vgeFrame.screenSize-vec2(.5))*.5+vec2(.5))),ivec2(0),size-1);
                vec2 sampleUv=CloudReconstructionUv(previous,vec2(0));
                accepted=length((sampleUv-oldUv)*vgeFrame.screenSize)<=2.83
                    &&CloudHistoryCompatible(meta,CloudHistoryFetch(2,previous),length(point),spacing,CloudHistoryFetch(1,previous).w);
            }
        }
    }
    if(accepted)
    {
        vec4 old0=CloudHistoryFetch(0,previous),old1=CloudHistoryFetch(1,previous);
        // Optical variation is the signal being accumulated. Current-neighborhood clamping
        // bounds changed lighting/weather without discarding ordinary sampling variance.
        accepted=CloudQuarterFinite(old0)&&CloudQuarterFinite(old1);
        if(accepted)
        {
            // Real samples keep the established temporal weight. Unsampled positions reuse
            // compatible history fully, bounded by current neighboring observations.
            float weight=sampled?cloudHistory.w:1.0;
            data[0]=mix(data[0],clamp(old0,low[0],high[0]),weight);
            data[1]=mix(data[1],clamp(old1,low[1],high[1]),weight);
        }
    }
    outCloudRadianceTransmission=vec4(max(data[0].rgb,vec3(0)),clamp(data[0].a,0.0,1.0));
    outCloudBucketTransmission=vec4(clamp(data[1].xyz,0.0,1.0),spacing);
    outCloudDepth=meta;
}
/** Keeps the existing half path while selecting quarter-to-half reconstruction through the shared parameter contract. */
void main()
{
    if(cloudLayout.z>1.5)CloudTemporalQuarter();else CloudTemporalHalf();
}
