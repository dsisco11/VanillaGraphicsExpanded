#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/vge_frame_ubo.glsl"
@import "./includes/pbr_scene_storage.glsl"
@import "./includes/oit.glsl"
@import "./includes/cloud_temporal_parameters.glsl"
@import "./includes/cloud_history.glsl"
@import "./includes/cloud_depth.glsl"

/** Reconstructs camera-axis foreground distance so planar surfaces agree across neighboring rays. */
float CloudResolveDepth(vec2 uv,float depth)
{
    vec4 p=vgeFrame.invProjectionMatrix*vec4(uv*2.0-1.0,depth*2.0-1.0,1);
    return abs(p.z/p.w);
}

/** Rejects foreground discontinuities, while allowing clouds wholly in front of both clips. */
float CloudResolveWeight(vec4 meta,vec2 sampleUv,vec2 currentDepth,float limit)
{
    if(any(isnan(meta))||any(isinf(meta))) return 0.0;
    vec4 direction=vgeFrame.invProjectionMatrix*vec4(sampleUv*2.0-1.0,1,1);
    float cloudFar=meta.y*abs(direction.z)/length(direction.xyz);
    // A partially occluded reduced ray cannot be split into front/back radiance during reconstruction.
    // Reject it conservatively rather than leaking integrated background cloud over new geometry.
    if(meta.y>0.0&&cloudFar>limit+max(.01,limit*.0001)) return 0.0;
    vec2 sampleDistance=vec2(CloudResolveDepth(sampleUv,meta.z),CloudResolveDepth(sampleUv,meta.w));
    if(meta.y>0.0&&cloudFar<min(limit,min(sampleDistance.x,sampleDistance.y))-2.0) return 1.0;
    vec2 difference=abs(sampleDistance-currentDepth)/max(vec2(2),currentDepth*.01);
    float error=max(difference.x,difference.y);
    return error<=1.0?1.0/(1.0+error):0.0;
}

/** Reconstructs all OIT channels together from reduced physical transport; never retraces rejected pixels. */
void main()
{
    if(!VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration())) discard;
    vec2 uv=gl_FragCoord.xy/vgeFrame.screenSize;
    vec2 depth=vec2(CloudResolveDepth(uv,CloudOpaqueDepth(uv)),
        CloudResolveDepth(uv,textureLod(liquidDepth,uv,0.0).r));
    float limit=min(depth.x,depth.y);
    vec2 position=(gl_FragCoord.xy-cloudTrace.zw-vec2(.5))*.5;
    ivec2 base=ivec2(floor(position)),size=ivec2(cloudTrace.xy); vec2 f=fract(position);
    vec4 value[6]; for(int i=0;i<6;i++) value[i]=vec4(0);
    float total=0.0;
    for(int y=0;y<2;y++) for(int x=0;x<2;x++)
    {
        ivec2 pixel=clamp(base+ivec2(x,y),ivec2(0),size-1);
        vec4 meta=CloudHistoryFetch(6,pixel);
        float weight=CloudResolveWeight(meta,CloudTraceUv(pixel,cloudTrace.zw),depth,limit)
            *(x==0?1.0-f.x:f.x)*(y==0?1.0-f.y:f.y);
        if(weight<=0.0) continue;
        total+=weight;
        for(int i=0;i<6;i++) value[i]+=weight*CloudHistoryFetch(i,pixel);
    }
    if(total<=1e-5)
    {
        // At a disocclusion, bilinear weights can be zero for the only depth-compatible neighbor.
        // Search a bounded reduced stencil instead of retracing or leaving an avoidable cloud hole.
        ivec2 center=clamp(ivec2(floor(position+vec2(.5))),ivec2(0),size-1),selected=center;
        float best=1e30;
        for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++)
        {
            ivec2 pixel=clamp(center+ivec2(x,y),ivec2(0),size-1);
            vec2 sampleUv=CloudTraceUv(pixel,cloudTrace.zw);
            float weight=CloudResolveWeight(CloudHistoryFetch(6,pixel),sampleUv,depth,limit);
            if(weight<=0.0) continue;
            vec2 delta=(sampleUv-uv)*vgeFrame.screenSize;
            float score=dot(delta,delta)/weight;
            if(score<best) { best=score; selected=pixel; }
        }
        if(best<1e30)
        {
            total=1.0;
            for(int i=0;i<6;i++) value[i]=CloudHistoryFetch(i,selected);
        }
    }
    if(total<=1e-5)
    {
        // No compatible foreground sample exists: submit neutral OIT, never a full-resolution march.
        outOitRevealBins=vec3(1); outRevealage=1.0; outOitGlow=vec4(0);
        outOitAccumulation0=vec4(0); outOitAccumulation1=vec4(0); outOitAccumulation2=vec4(0);
        return;
    }
    for(int i=0;i<6;i++) value[i]/=total;
    outOitRevealBins=clamp(value[0].xyz,0.0,1.0); outRevealage=clamp(value[1].x,0.0,1.0);
    outOitGlow=vec4(0,0,0,clamp(value[2].a,0.0,1.0));
    // History remains physical. Encode only accumulated RGB once at submission; weighted alpha is untouched.
    outOitAccumulation0=vec4(VgeStoreSceneLighting(value[3].rgb),value[3].a);
    outOitAccumulation1=vec4(VgeStoreSceneLighting(value[4].rgb),value[4].a);
    outOitAccumulation2=vec4(VgeStoreSceneLighting(value[5].rgb),value[5].a);
}
