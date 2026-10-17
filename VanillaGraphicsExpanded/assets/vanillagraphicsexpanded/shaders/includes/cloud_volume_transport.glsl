@import "./cloud_volume_parameters.glsl"
@import "./cloud_volume_sampling.glsl"
@import "./cloud_volume_density.glsl"
#ifdef VGE_CLOUD_HIERARCHY
@import "./cloud_volume_hierarchy.glsl"
#endif
@import "./cloud_volume_light.glsl"
@import "./atmosphere_aerial.glsl"
@import "./pbr_scene_storage.glsl"
@import "./pbr_color.glsl"
#ifdef VGE_CLOUD_PHYSICAL
layout(location=0) out vec3 outOitRevealBins;
layout(location=1) out float outRevealage;
layout(location=2) out vec4 outOitAccumulation0;
layout(location=3) out vec4 outOitAccumulation1;
layout(location=4) out vec4 outOitAccumulation2;
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
/** Bounded anisotropic single scattering plus exactly one isotropic approximate octave. */
vec3 CloudDirectional(vec3 local,vec3 view,vec3 light,vec3 illuminance,float jitter)
{
    if(max(illuminance.r,max(illuminance.g,illuminance.b))<=0.0) return vec3(0);
    float tau=CloudLightDepth(local,light,jitter), mu=clamp(dot(view,light),-1.0,1.0);
    float phase=.64/(12.5663706144*pow(1.36-1.2*mu,1.5));
    return illuminance*(phase*exp(-tau)+volumeSamples.z/12.5663706144*exp(-volumeSamples.w*tau));
}
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
/** Keeps all native depth buckets and separate attenuation with first/last contributing depth. */
struct CloudPayload { vec3 bins; float reveal; vec4 glow; vec4 a0; vec4 a1; vec4 a2; vec4 depth; };
/** Initializes neutral cloud transport, including both view-specific depth clips. */
CloudPayload CloudEmpty(vec2 uv)
{
    return CloudPayload(vec3(1),1.0,vec4(0),vec4(0),vec4(0),vec4(0),
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
    CloudRay ray;
    if(!CloudStart(origin,direction,limit,ray)) return;
    vec2 jitter=volumeDetail.w>.5?CloudRayJitter(uv):vec2(.5);
    vec4 k=vec4(0); vec3 reveal=vec3(1); float transmission=1.0;
    for(int cellIndex=0;cellIndex<402;cellIndex++)
    {
#ifdef VGE_CLOUD_HIERARCHY
        if(!CloudHierarchySeek(ray)) break;
#endif
        vec4 data=CloudColumn(ray.cell); float near,far;
        if(CloudInterval(ray,data,near,far))
        {
            int count=volumeDetail.w>.5?int(volumeSamples.x):1;
            float ds=(far-near)/float(count);
            // Shift the stratified samples inside this occupied interval without changing its quadrature weights.
            float phase=volumeDetail.w>.5?CloudSegmentJitter(jitter.x,ray.cell,0,0x56494557u):.5;
            for(int i=0;i<16;i++)
            {
                if(i>=count) break;
                float t=near+(float(i)+phase)*ds;
                vec3 local=ray.o+direction*t;
                float sigma=CloudDensity(local,ray.cell,data);
                float alpha=clamp(1.0-exp(-sigma*(ds*volumeAnchorUnits.z)),0.0,1.0);
                if(alpha<=0.0) continue;
                if(result.depth.y==0.0) result.depth.x=near;
                result.depth.y=far;
                vec3 source=volumeEnvironment.rgb
                    +CloudDirectional(local,direction,volumeSun.xyz,volumeSolar.rgb,CloudSegmentJitter(jitter.y,ray.cell,i,0x53554E00u))
                    +CloudDirectional(local,direction,volumeMoon.xyz,volumeLunar.rgb,CloudSegmentJitter(jitter.y,ray.cell,i,0x4D4F4F4Eu));
                source=CloudTransport(source,direction*t);
                // Encode only RGB, before view coverage and native bucket weighting.
                k.rgb+=transmission*alpha*CloudEncode(source);
                transmission*=1.0-alpha;
                float bin=log(CloudAdjustedDepth(near+float(i)*ds)/30.0+1.0);
                vec3 b=vec3(vgeOitBellCurve(bin),vgeOitBellCurve(bin-1.0),bin>2.0?1.0:vgeOitBellCurve(bin-2.0));
                reveal*=clamp(vec3(1)-alpha*b,0.0,1.0);
            }
        }
        if(transmission<1e-6||!CloudAdvance(ray)) break;
    }
    k.a=clamp(1.0-transmission,0.0,1.0);
    result.bins=clamp(reveal,0.0,1.0); result.reveal=clamp(transmission,0.0,1.0);
    result.a0=k*(1.0-reveal.x); result.a1=k*(1.0-reveal.y); result.a2=k*(1.0-reveal.z);
    result.glow=vec4(0,0,0,k.a);
}
/** Routes intact native payload for replacement or existing OIT blending. */
void CloudWrite(CloudPayload p)
{
    outOitRevealBins=p.bins; outRevealage=p.reveal;
#ifndef VGE_CLOUD_PHYSICAL
    outOitGlow=p.glow;
#endif
    outOitAccumulation0=p.a0; outOitAccumulation1=p.a1; outOitAccumulation2=p.a2;
}
