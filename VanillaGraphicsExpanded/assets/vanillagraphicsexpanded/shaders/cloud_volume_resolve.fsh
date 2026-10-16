#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/cloud_volume_transport.glsl"
@import "./includes/cloud_volume_visibility.glsl"
@import "./includes/cloud_temporal_parameters.glsl"
@import "./includes/cloud_history.glsl"
/** Rejects foreground changes and cloud-visibility discontinuities before weighting any OIT channel. */
bool CloudSpatialCompatible(CloudPayload actual,ivec2 pixel)
{
    vec4 meta=CloudHistoryFetch(6,pixel);
    if(any(isnan(meta))||any(isinf(meta))||meta.y<=0.0) return false;
    if(max(abs(meta.z-actual.depth.z),abs(meta.w-actual.depth.w))>2e-5) return false;
    float tolerance=max(2.0,actual.depth.y*.01);
    if(max(abs(meta.x-actual.depth.x),abs(meta.y-actual.depth.y))>tolerance) return false;
    if(abs(CloudHistoryFetch(1,pixel).x-actual.reveal)>.02) return false;
    return all(lessThanEqual(abs(CloudHistoryFetch(0,pixel).xyz-actual.bins),vec3(.02)));
}
/** Upsamples the intact payload with shared depth-aware weights and traces rejected silhouettes exactly. */
void main()
{
    if(!VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration())) discard;
    vec2 uv=gl_FragCoord.xy/vgeFrame.screenSize;
    CloudPayload actual=CloudEmpty(uv); CloudVisibility(uv,actual);
    if(actual.reveal>=1.0) { CloudWrite(actual); return; }
    vec2 position=(gl_FragCoord.xy-cloudTrace.zw-vec2(.5))*.5;
    ivec2 base=ivec2(floor(position)),size=ivec2(cloudTrace.xy); vec2 f=fract(position);
    vec4 value[6]; for(int i=0;i<6;i++) value[i]=vec4(0);
    float total=0.0;
    for(int y=0;y<2;y++) for(int x=0;x<2;x++)
    {
        ivec2 pixel=clamp(base+ivec2(x,y),ivec2(0),size-1);
        if(!CloudSpatialCompatible(actual,pixel)) continue;
        float weight=(x==0?1.0-f.x:f.x)*(y==0?1.0-f.y:f.y);
        total+=weight;
        for(int i=0;i<6;i++) value[i]+=weight*CloudHistoryFetch(i,pixel);
    }
    if(total<=1e-5)
    {
        CloudPayload exact=CloudEmpty(uv); CloudIntegrate(uv,exact); CloudWrite(exact); return;
    }
    for(int i=0;i<6;i++) value[i]/=total;
    outOitRevealBins=clamp(value[0].xyz,0.0,1.0); outRevealage=clamp(value[1].x,0.0,1.0);
    outOitGlow=vec4(0,0,0,clamp(value[2].a,0.0,1.0));
    // History remains physical. Encode only accumulated RGB once at submission; weighted alpha is untouched.
    outOitAccumulation0=vec4(VgeStoreSceneLighting(value[3].rgb),value[3].a);
    outOitAccumulation1=vec4(VgeStoreSceneLighting(value[4].rgb),value[4].a);
    outOitAccumulation2=vec4(VgeStoreSceneLighting(value[5].rgb),value[5].a);
}
