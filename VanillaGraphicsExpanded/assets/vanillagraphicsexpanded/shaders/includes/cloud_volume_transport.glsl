@import "./cloud_volume_parameters.glsl"
@import "./cloud_volume_sampling.glsl"
@import "./cloud_volume_density.glsl"
@import "./cloud_volume_primary.glsl"
#ifdef VGE_CLOUD_HIERARCHY
@import "./cloud_volume_hierarchy.glsl"
#endif
@import "./cloud_volume_light.glsl"
@import "./atmosphere_aerial.glsl"
@import "./pbr_scene_storage.glsl"
@import "./pbr_color.glsl"
#ifdef VGE_CLOUD_PHYSICAL
layout(location=0) out vec4 outCloudRadianceTransmission;
layout(location=1) out vec4 outCloudBucketTransmission;
/** Weights one cloud contribution in the native transparency depth bins. */
float vgeOitBellCurve(float t) { float n=t/.832; return exp(-n*n); }
#else
@import "./oit.glsl"
#endif
@import "./cloud_depth.glsl"

/** Reconstructs this view's camera-relative position with its own inverse projection/view. */
vec3 CloudUnproject(vec2 ndc,float depth)
{
    vec4 p=vgeFrame.invProjectionMatrix*vec4(ndc,depth*2.0-1.0,1);
    p/=p.w;
    return (vgeFrame.invViewMatrix*vec4(p.xyz,0)).xyz;
}
/** Retains native volumetric halfsmooth distance for each integration interval's OIT bins. */
float CloudAdjustedDepth(float x) { return x>500.0?x-250.0:x*x*x*(1.0-x/1000.0)/250000.0; }
/** Applies local underwater fog instead of an additional atmosphere integration. */
vec3 CloudTransport(vec3 source,vec3 displacement)
{
    if(volumeAerial.z<=.7) return VgeApplyAerial(source,displacement,1.0,volumeAerial.xyw,volumeSun.xyz);
    float depth=length(displacement),clamped=min(depth,250.0);
    float extra=max(-volumeFog.z*clamped*volumeFog.w/60.0,0.0);
    float fog=max(1.0-exp(-(displacement.y-volumeFog.w)*volumeFog.z),1.0-exp(-clamped*volumeFog.x-extra));
    fog=max(min(.04,fog),fog-clamp((8.0-depth)/8.0,0.0,.9));
    return mix(source,VgeCompatibilityLuminance(volumeFogColor.rgb),clamp(fog+volumeFog.y,0.0,1.0));
}
/** Physical history defers frame encoding until the full-resolution submission. */
vec3 CloudEncode(vec3 source)
{
#ifdef VGE_CLOUD_PHYSICAL
    return source;
#else
    return VgeStoreSceneLighting(source);
#endif
}
/** Retains integrated radiance and attenuation independently of the final native OIT products. */
struct CloudPayload { vec3 bins; float reveal; vec3 radiance; vec4 depth; };
/** Initializes neutral cloud transport, including both view-specific depth clips. */
CloudPayload CloudEmpty(vec2 uv)
{
    return CloudPayload(vec3(1),1.0,vec3(0),
        vec4(0,0,CloudOpaqueDepth(uv),textureLod(liquidDepth,uv,0.0).r));
}
/** Evaluates identical transport for reference and reduced traces. */
void CloudIntegrate(vec2 uv,inout CloudPayload result)
{
    vec2 ndc=uv*2.0-1.0;
    vec3 direction=normalize(CloudUnproject(ndc,1.0));
    // Native clips at both opaque and liquid depth. Normalized UV handles quarter-size liquid storage.
    float limit=min(length(CloudUnproject(ndc,result.depth.z)),length(CloudUnproject(ndc,result.depth.w)));
    vec3 origin=-volumeOffsetWidth.xyz;
    origin.xz+=volumeOffsetWidth.w*25.0;
    vec2 span;
    if(!CloudPrimarySpan(origin,direction,limit,span)) return;
    int count=CloudPrimaryCount(span.y-span.x);
    float ds=(span.y-span.x)/float(count);
    float primaryJitter=CloudPrimaryJitter(uv);
    vec4 k=vec4(0); vec3 reveal=vec3(1); float transmission=1.0;
    for(int i=0;i<VGE_CLOUD_PRIMARY_LIMIT;i++)
    {
        if(i>=count) break;
        float t=CloudPrimaryPosition(span,count,i,primaryJitter);
        vec3 local=origin+direction*t;
        ivec2 cell=ivec2(floor(local.xz/50.0));
        vec4 data=CloudColumn(cell);
        // Cheap support rejection never redistributes samples or restarts a cell-local budget.
        if(data.r<=0.0 || local.y<data.b*50.0 || local.y>data.a*50.0) continue;
        float sigma=CloudDensity(local,cell,data);
        float alpha=clamp(1.0-exp(-sigma*(ds*volumeAnchorUnits.z)),0.0,1.0);
        if(alpha<=0.0) continue;
        float near=span.x+float(i)*ds,far=min(span.y,near+ds);
        if(result.depth.y==0.0) result.depth.x=near;
        result.depth.y=far;
        vec3 source=volumeEnvironment.rgb
            +CloudDirectional(local,direction,volumeSun.xyz,volumeSolar.rgb)
            +CloudDirectional(local,direction,volumeMoon.xyz,volumeLunar.rgb);
        source=CloudTransport(source,direction*t);
        // Encode only RGB, before view coverage and native bucket weighting.
        k.rgb+=transmission*alpha*CloudEncode(source);
        transmission*=1.0-alpha;
        float bin=log(CloudAdjustedDepth(near)/30.0+1.0);
        vec3 b=vec3(vgeOitBellCurve(bin),vgeOitBellCurve(bin-1.0),bin>2.0?1.0:vgeOitBellCurve(bin-2.0));
        reveal*=clamp(vec3(1)-alpha*b,0.0,1.0);
        // Terminate only after this sample has contributed every optical product and support bound.
        // A zero threshold admits the entire finite schedule, even after exact saturation.
        if(transmission<volumePrimary.w) break;
    }
    result.bins=clamp(reveal,0.0,1.0); result.reveal=clamp(transmission,0.0,1.0);
    result.radiance=k.rgb;
}
/** Stores physical factors for reconstruction, or submits native products for the direct reference. */
void CloudWrite(CloudPayload p)
{
#ifdef VGE_CLOUD_PHYSICAL
    outCloudRadianceTransmission=vec4(p.radiance,p.reveal);
    outCloudBucketTransmission=vec4(p.bins,0);
#else
    // The reference retains its original encoding-before-weighting contract without reconstruction.
    vec4 k=vec4(p.radiance,clamp(1.0-p.reveal,0.0,1.0));
    outOitRevealBins=p.bins; outRevealage=p.reveal; outOitGlow=vec4(0,0,0,k.a);
    outOitAccumulation0=k*(1.0-p.bins.x);
    outOitAccumulation1=k*(1.0-p.bins.y);
    outOitAccumulation2=k*(1.0-p.bins.z);
#endif
}
