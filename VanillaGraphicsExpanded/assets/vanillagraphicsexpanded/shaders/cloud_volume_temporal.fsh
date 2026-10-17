#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/vge_frame_ubo.glsl"
@import "./includes/cloud_temporal_parameters.glsl"
@import "./includes/cloud_history.glsl"
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
/** Requires compatible cloud support and both foreground clips before accepting any channel. */
bool CloudHistoryCompatible(vec4 a,vec4 b,float expected)
{
    if(b.y<=0.0) return false;
    if(any(isnan(b))||any(isinf(b))) return false;
    if(max(abs(a.z-b.z),abs(a.w-b.w))>2e-5) return false;
    if(abs((b.x+b.y)*.5-expected)>max(2.0,expected*.02)) return false;
    return abs((b.y-b.x)-(a.y-a.x))<=max(2.0,(a.y-a.x)*.05);
}
/** Filters physical radiance and attenuation factors together; depth remains the current trace receipt. */
void main()
{
    ivec2 pixel=ivec2(gl_FragCoord.xy), size=ivec2(cloudTrace.xy);
    vec4 data[3]; for(int i=0;i<3;i++) data[i]=CloudCurrentFetch(i,pixel);
    vec4 meta=data[2]; bool accepted=false; ivec2 previous=ivec2(0);
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
                    accepted=CloudHistoryCompatible(meta,CloudHistoryFetch(2,previous),length(point));
            }
        }
    }
    if(accepted)
    {
        // A fresh upload is not necessarily a discontinuity. Reject rapid optical changes
        // explicitly, then bound gradual weather and self-shadow changes by current samples.
        if(abs(data[0].a-CloudHistoryFetch(0,previous).a)>.05) accepted=false;
        if(any(greaterThan(abs(data[1].xyz-CloudHistoryFetch(1,previous).xyz),vec3(.05)))) accepted=false;
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
            if(!CloudHistoryCompatible(meta,m,(meta.x+meta.y)*.5)) continue;
            for(int i=0;i<2;i++) { vec4 c=CloudCurrentFetch(i,n); low[i]=min(low[i],c); high[i]=max(high[i],c); }
        }
        if(finite) for(int i=0;i<2;i++) data[i]=mix(data[i],clamp(old[i],low[i],high[i]),cloudHistory.w);
    }
    outCloudRadianceTransmission=vec4(max(data[0].rgb,vec3(0)),clamp(data[0].a,0.0,1.0));
    outCloudBucketTransmission=vec4(clamp(data[1].xyz,0.0,1.0),0);
    outCloudDepth=meta;
}
