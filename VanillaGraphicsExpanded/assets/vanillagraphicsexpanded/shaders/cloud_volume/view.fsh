#version 430 core
@import "traversal.glsl"
@import "../includes/vge_frame_ubo.glsl"
#if VGE_STORED_VIEW_TRACE_DIVISOR != 1
@import "../includes/cloud_temporal_parameters.glsl"
@import "../includes/blue_noise.glsl"
@import "../includes/interleaved_sampling.glsl"
uniform sampler2D viewBlueNoise;
#endif
/** Maps physically compact rays onto their blue-noise-selected reduced-grid positions. */
vec2 StoredViewUv()
{
    vec2 size=ceil(vgeFrame.screenSize/float(VGE_STORED_VIEW_TRACE_DIVISOR));
    #if VGE_STORED_VIEW_TRACE_DIVISOR != 1
    if(cloudHistory.x>.5)
    {
        ivec2 tile=ivec2(gl_FragCoord.xy);
        float rank=BlueNoiseRank(viewBlueNoise,tile,0u,8);
        return (vec2(InterleavedSampleLocation2x2(rank,tile,vgeFrame.frameIndex,ivec2(size)))+.5)/size;
    }
    #endif
    return gl_FragCoord.xy/size;
}
layout(std140) uniform StoredVolumeView
{
    ivec4 viewSize,viewSlots;
    vec4 viewFirst,viewCell,viewMinimum,viewMaximum,viewOrigin;
    vec4 viewBudget,viewSolar,viewLunar,viewEnvironment,viewSun,viewMoon,viewAerial,viewFlatFog;
};
#if VGE_STORED_VIEW_SHADOWS
uniform sampler3D viewDirect;
#endif
#if VGE_STORED_VIEW_DIFFUSE
uniform sampler3D viewDiffuse;
#endif
layout(std430) readonly buffer ViewRegions {uint viewRegions[];};
uniform sampler2D viewMaterialDepth;
 uniform sampler2D viewVisibilityDepth;
 uniform sampler2D viewLiquidDepth;
#define VGE_ATMOSPHERE_SCATTERING_DIRECTIONS
#define vge_atmosphereScatteringSun viewSun.xyz
#define vge_atmosphereScatteringMoon viewMoon.xyz
#define VGE_ATMOSPHERE_FRAGMENT_COORD (StoredViewUv()*vgeFrame.screenSize)
@import "../includes/atmosphere_aerial.glsl"
@import "../includes/pbr_scene_storage.glsl"
@import "../includes/pbr_color.glsl"
#if VGE_STORED_VIEW_TRACE_DIVISOR == 1
@import "../includes/oit.glsl"
#else
layout(location=0) out vec4 outViewCloud;
layout(location=1) out vec3 outViewBins;
layout(location=2) out vec4 outViewMetadata;
#endif
/** Reconstructs rotation-only camera displacement; native and material placement enter once through viewOrigin. */
vec3 StoredViewUnproject(vec2 ndc,float depth)
{
    vec4 p=vgeFrame.invProjectionMatrix*vec4(ndc,depth*2-1,1);
    return (vgeFrame.invViewMatrix*vec4(p.xyz/p.w,0)).xyz;
}
/** Evaluates the unit-integral HG response for the outgoing sample-to-camera direction. */
float StoredViewPhase(vec3 outgoing,vec3 light)
{
    float d=1.36-1.2*clamp(dot(outgoing,light),-1.0,1.0);
    return .64/(12.566370614359172*d*sqrt(d));
}
/** Composes current directional first scattering and the retained isotropic source once. */
vec3 StoredViewSource(ivec3 cell,vec2 phases,float sigma)
{
    if(viewBudget.w>0)
    {
        ivec3 p=cell;
        p=clamp(p,ivec3(0),viewSize.xyz-1);ivec3 b=p/ivec3(8,4,8);
        int id=(b.z*viewSlots.w+b.y)*viewSlots.z+b.x;
        int mode=int(viewBudget.w);vec3 color;
        if(mode==1)color=vec3(1-exp(-sigma*max(viewCell.x,max(viewCell.y,viewCell.z))));
        else if(mode==2)color=fract(vec3(id)*vec3(.6180339,.4142135,.7320508));
        else if(mode==3)
        {
            uint age=vgeFrame.frameIndex-viewRegions[id*16+15];
            color=mix(vec3(.1,1,.1),vec3(1,.05,.05),clamp(float(age)/600.0,0,1));
        }
        else
        {
            bool missing=false;
            #if VGE_STORED_VIEW_SHADOWS
            missing=(any(greaterThan(viewSolar.rgb,vec3(0)))&&viewRegions[id*16+6]==0u)||
                (any(greaterThan(viewLunar.rgb,vec3(0)))&&viewRegions[id*16+8]==0u);
            #endif
            #if VGE_STORED_VIEW_DIFFUSE
            missing=missing||viewRegions[id*16+13]==0u;
            #endif
            color=missing?vec3(1,.05,.05):vec3(.05,1,.05);
        }
        return color*viewEnvironment.w;
    }
    vec2 visibility=vec2(1);
    #if VGE_STORED_VIEW_SHADOWS
    visibility=texelFetch(viewDirect,cell,0).rg;
    #endif
    vec3 diffuse=.9*viewEnvironment.rgb;
    #if VGE_STORED_VIEW_DIFFUSE
    diffuse=texelFetch(viewDiffuse,cell,0).rgb*viewBudget.y;
    #endif
    return .9*(viewSolar.rgb*visibility.x*phases.x+viewLunar.rgb*visibility.y*phases.y)+diffuse;
}
/** Applies the shared affine atmospheric or local underwater medium once to the representative physical source. */
vec3 StoredViewMedium(vec3 source,vec3 displacement)
{
    if(viewAerial.z<=.7)return VgeApplyAerial(source,displacement,1,viewAerial.xyw,viewSun.xyz);
    float depth=length(displacement),clipped=min(depth,250);
    float extra=max(-viewFlatFog.x*clipped*viewFlatFog.y/60,0);
    float fog=max(1-exp(-(displacement.y-viewFlatFog.y)*viewFlatFog.x),1-exp(-clipped*vgeFrame.fog0.w-extra));
    fog=max(min(.04,fog),fog-clamp((8-depth)/8,0,.9));
    return mix(source,VgeCompatibilityLuminance(vgeFrame.fog0.rgb),clamp(fog+vgeFrame.fogMinimum,0,1));
}
/** Evaluates the native Gaussian bucket response without declaring native framebuffer outputs. */
float StoredViewBellCurve(float distance)
{
    float n=distance/.832;
    return exp(-n*n);
}
/** Preserves the native bucket-depth convention at every integrated interval's near endpoint. */
vec3 StoredViewBins(float distance)
{
    float adjusted=distance>500?distance-250:distance*distance*distance*(1-distance/1000)/250000;
    float bin=log(1+adjusted/30);
    return vec3(StoredViewBellCurve(bin),StoredViewBellCurve(bin-1),bin>2?1:StoredViewBellCurve(bin-2));
}
/** Writes native products directly at full size or compact values at the compiled reduced scale. */
void StoredViewWrite(vec4 cloud,vec3 bins,float depth,bool empty,float representative,float visits)
{
    #if VGE_STORED_VIEW_TRACE_DIVISOR == 1
    if(empty)discard;
    cloud.rgb=VgeStoreSceneLighting(cloud.rgb);
    outOitRevealBins=bins;outRevealage=clamp(1-cloud.a,0,1);outOitGlow=vec4(0,0,0,cloud.a);
    outOitAccumulation0=cloud*(1-bins.x);outOitAccumulation1=cloud*(1-bins.y);outOitAccumulation2=cloud*(1-bins.z);
    #else
    outViewCloud=cloud;outViewBins=bins;outViewMetadata=vec4(depth,empty?2:1,representative,visits);
    #endif
}
/** Clips a full pixel directly or conservatively gathers the complete reduced pixel's depth footprint. */
float StoredViewDepth(vec2 uv)
{
    #if VGE_STORED_VIEW_TRACE_DIVISOR == 1
    return min(textureLod(viewMaterialDepth,uv,0).r,
        min(textureLod(viewVisibilityDepth,uv,0).r,textureLod(viewLiquidDepth,uv,0).r));
    #else
    vec4 depths=vec4(3.402823e38);
    // Half uses one 2x2 gather per source; quarter uses four to cover a 4x4 footprint.
    for(int y=0;y<VGE_STORED_VIEW_TRACE_DIVISOR/2;y++)for(int x=0;x<VGE_STORED_VIEW_TRACE_DIVISOR/2;x++)
    {
        vec2 tap=uv+(vec2(x,y)*2-float(VGE_STORED_VIEW_TRACE_DIVISOR/2-1))/vgeFrame.screenSize;
        depths=min(depths,min(textureGather(viewMaterialDepth,tap,0),
            min(textureGather(viewVisibilityDepth,tap,0),textureGather(viewLiquidDepth,tap,0))));
    }
    return min(min(depths.x,depths.y),min(depths.z,depths.w));
    #endif
}
/** Integrates authoritative constant cells at the configured size along exact clipped cell intervals. */
void main()
{
    if(!VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration()))discard;
    vec2 uv=StoredViewUv(),ndc=uv*2-1;
    vec3 direction=normalize(StoredViewUnproject(ndc,1));
    float depth=StoredViewDepth(uv);
    float limit=length(StoredViewUnproject(ndc,depth));
    CloudVolumeGrid grid;grid.size=viewSize.xyz;grid.blocks=ivec3(viewSlots.z,viewSlots.w,viewSlots.z);grid.slot=viewSlots.xy;
    grid.first=viewFirst.xyz;grid.cell=viewCell.xyz;grid.minimum=viewMinimum.xyz;grid.maximum=viewMaximum.xyz;
    CloudVolumeRay ray=CloudVolumeBegin(grid,viewOrigin.xyz,direction,limit);
    if(ray.failed)discard;
    if(ray.t>=ray.end){StoredViewWrite(vec4(0),vec3(1),depth,true,0,0);return;}
    vec2 phases=vec2(StoredViewPhase(-direction,viewSun.xyz),StoredViewPhase(-direction,viewMoon.xyz));
    vec3 bins=vec3(1),radiance=vec3(0);float transmission=1,weightSum=0,moment=0;bool terminated=false;
    for(int i=0;i<2048;i++)
    {
        vec2 interval;bool occupied;if(!CloudVolumeNext(grid,ray,interval,occupied))break;
        if(!occupied)continue;
        float t=(interval.x+interval.y)*.5;
        ivec3 cell=ray.sampleCell;
        float sigma=texelFetch(volumeDensity,cell,0).r;
        if(isnan(sigma)||isinf(sigma)||sigma<0)discard;
        float alpha=1-exp(-sigma*(interval.y-interval.x));
        if(alpha<=0)continue;
        float weight=transmission*alpha;
        radiance+=weight*StoredViewSource(cell,phases,sigma);weightSum+=weight;moment+=weight*t;
        transmission*=1-alpha;bins*=clamp(vec3(1)-alpha*StoredViewBins(interval.x),0,1);
        #if VGE_STORED_VIEW_TERMINATION
        if(transmission<viewBudget.x){terminated=true;break;}
        #endif
    }
    if(!terminated&&!CloudVolumeComplete(ray))discard; // A safety-limit prefix is never accepted as completed transport.
    if(weightSum==0){StoredViewWrite(vec4(0),vec3(1),depth,true,0,float(ray.operations));return;}
    if(weightSum>0)radiance=StoredViewMedium(radiance/weightSum,direction*(moment/weightSum))*weightSum;
    if(any(isnan(radiance))||any(isinf(radiance)))discard;
    float opacity=clamp(1-transmission,0,1);
    StoredViewWrite(vec4(radiance,opacity),clamp(bins,0,1),depth,false,moment/weightSum,float(ray.operations));
}
