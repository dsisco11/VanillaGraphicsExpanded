#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/vge_frame_ubo.glsl"
@import "./includes/pbr_scene_storage.glsl"
@import "./includes/oit.glsl"
@import "./includes/cloud_temporal_parameters.glsl"
@import "./includes/cloud_history.glsl"
@import "./includes/cloud_depth.glsl"

@import "./includes/cloud_reconstruction_support.glsl"

/** Reconstructs each tap's native products before interpolation to retain spatial color/coverage covariance. */
void CloudResolveAccumulate(inout vec4 value[4],ivec2 pixel,float weight)
{
    vec4 radianceTransmission=CloudHistoryFetch(0,pixel);
    vec3 buckets=clamp(CloudHistoryFetch(1,pixel).xyz,0.0,1.0);
    float transmission=clamp(radianceTransmission.a,0.0,1.0);
    vec4 radianceOpacity=vec4(max(radianceTransmission.rgb,vec3(0)),1.0-transmission);
    value[0]+=weight*vec4(buckets,transmission);
    value[1]+=weight*(radianceOpacity*(1.0-buckets.x));
    value[2]+=weight*(radianceOpacity*(1.0-buckets.y));
    value[3]+=weight*(radianceOpacity*(1.0-buckets.z));
}

/** Reconstructs all OIT channels together from reduced physical transport; never retraces rejected pixels. */
void main()
{
    if(!VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration())) discard;
    vec2 uv=gl_FragCoord.xy/vgeFrame.screenSize;
    vec2 depth=vec2(CloudReconstructionDepth(uv,CloudOpaqueDepth(uv)),
        CloudReconstructionDepth(uv,textureLod(liquidDepth,uv,0.0).r));
    float limit=min(depth.x,depth.y);
    vec2 phase=cloudLayout.z>1.5?vec2(0):cloudTrace.zw;
    vec2 position=(gl_FragCoord.xy-phase-vec2(.5))*.5;
    ivec2 base=ivec2(floor(position)),size=ivec2(cloudTrace.xy); vec2 f=fract(position);
    vec4 value[4]; for(int i=0;i<4;i++) value[i]=vec4(0);
    float total=0.0;
    for(int y=0;y<2;y++) for(int x=0;x<2;x++)
    {
        ivec2 pixel=clamp(base+ivec2(x,y),ivec2(0),size-1);
        vec4 meta=CloudHistoryFetch(2,pixel);
        float weight=CloudReconstructionWeight(meta,CloudReconstructionUv(pixel,cloudTrace.zw),depth,limit)
            *(x==0?1.0-f.x:f.x)*(y==0?1.0-f.y:f.y);
        if(weight<=0.0) continue;
        total+=weight;
        CloudResolveAccumulate(value,pixel,weight);
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
            vec2 sampleUv=CloudReconstructionUv(pixel,cloudTrace.zw);
            float weight=CloudReconstructionWeight(CloudHistoryFetch(2,pixel),sampleUv,depth,limit);
            if(weight<=0.0) continue;
            vec2 delta=(sampleUv-uv)*vgeFrame.screenSize;
            float score=dot(delta,delta)/weight;
            if(score<best) { best=score; selected=pixel; }
        }
        if(best<1e30)
        {
            total=1.0;
            for(int i=0;i<4;i++) value[i]=vec4(0);
            CloudResolveAccumulate(value,selected,1.0);
        }
    }
    if(total<=1e-5)
    {
        // No compatible foreground sample exists: submit neutral OIT, never a full-resolution march.
        outOitRevealBins=vec3(1); outRevealage=1.0; outOitGlow=vec4(0);
        outOitAccumulation0=vec4(0); outOitAccumulation1=vec4(0); outOitAccumulation2=vec4(0);
        return;
    }
    for(int i=0;i<4;i++) value[i]/=total;
    outOitRevealBins=clamp(value[0].xyz,0.0,1.0); outRevealage=clamp(value[0].a,0.0,1.0);
    outOitGlow=vec4(0,0,0,1.0-outRevealage);
    // Encode only the reconstructed, spatially filtered products at final submission.
    outOitAccumulation0=vec4(VgeStoreSceneLighting(value[1].rgb),value[1].a);
    outOitAccumulation1=vec4(VgeStoreSceneLighting(value[2].rgb),value[2].a);
    outOitAccumulation2=vec4(VgeStoreSceneLighting(value[3].rgb),value[3].a);
}
