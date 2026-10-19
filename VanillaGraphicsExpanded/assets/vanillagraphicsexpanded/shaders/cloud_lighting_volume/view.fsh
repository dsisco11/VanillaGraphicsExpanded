#version 430 core
@import "./parameters.glsl"
@import "./density.glsl"
@import "./lookup.glsl"
@import "../includes/vge_frame_ubo.glsl"
layout(std140) uniform LightingViewInputs
{
    vec4 viewBudget,viewSolar,viewLunar,viewEnvironment,viewSun,viewMoon,viewAerial,viewFog,viewFogColor;
};
layout(std430) readonly buffer ViewScratch { uint viewScratch[]; };
uniform sampler2D viewMaterialDepth;
uniform sampler2D viewVisibilityDepth;
uniform sampler2D viewLiquidDepth;
#define VGE_ATMOSPHERE_SCATTERING_DIRECTIONS
#define vge_atmosphereScatteringSun viewSun.xyz
#define vge_atmosphereScatteringMoon viewMoon.xyz
@import "../includes/atmosphere_aerial.glsl"
@import "../includes/pbr_scene_storage.glsl"
@import "../includes/pbr_color.glsl"
@import "../includes/oit.glsl"

/** Reconstructs camera-relative displacement without duplicating the native placement translation. */
vec3 ViewUnproject(vec2 ndc,float depth)
{
    vec4 p=vgeFrame.invProjectionMatrix*vec4(ndc,depth*2.0-1.0,1);
    return (vgeFrame.invViewMatrix*vec4(p.xyz/p.w,0)).xyz;
}
/** Clips a finite current interval with exact-zero parallel handling, never wrapping transport. */
bool ViewSlab(float origin,float direction,float lo,float hi,inout vec2 span)
{
    if(direction==0.0) return origin>=lo&&origin<=hi;
    float a=(lo-origin)/direction,b=(hi-origin)/direction;
    span=vec2(max(span.x,min(a,b)),min(span.y,max(a,b)));
    return span.y>span.x;
}
/** Admits the finite native horizontal domain and current occupied height envelope before budgeting samples. */
bool ViewSpan(vec3 origin,vec3 direction,float limit,out vec2 span)
{
    span=vec2(0,limit);float width=candidateOffsetWidth.w*50.0;
    if(!ViewSlab(origin.x,direction.x,0,width,span)||!ViewSlab(origin.z,direction.z,0,width,span)) return false;
    float lo=uintBitsToFloat(viewScratch[1536]),hi=uintBitsToFloat(viewScratch[1537]);
    if(lo>hi) return false;
    // Widen only for binary32 boundary uncertainty; an inclusive thin native layer remains supported.
    float bound=max(1.0,max(max(abs(lo),abs(hi)),abs(origin.y)+abs(direction.y)*span.y));
    float epsilon=8.0*1.1920928955078125e-7*bound;
    return ViewSlab(origin.y,direction.y,lo-epsilon,hi+epsilon,span);
}
/** Evaluates the nominated outward receiver-to-source HG response per steradian. */
float ViewPhase(vec3 direction,vec3 light)
{
    float denominator=1.36-1.2*clamp(dot(direction,light),-1.0,1.0);
    return 0.64/(12.566370614359172*denominator*sqrt(denominator));
}
/** Applies current physical sources to retained transmission and diffuse source, with explicit channel fallback. */
vec3 ViewSource(vec3 q,vec2 phases)
{
    vec2 visibility=vec2(1);
#if VGE_LIGHTING_VOLUME_DIRECT
    if(any(greaterThan(viewSolar.rgb,vec3(0)))) visibility.x=CandidateTransmission(q,0);
    if(any(greaterThan(viewLunar.rgb,vec3(0)))) visibility.y=CandidateTransmission(q,1);
#endif
    vec3 diffuse=0.9*viewEnvironment.rgb;
#if VGE_LIGHTING_VOLUME_DIFFUSE
    diffuse=CandidateDiffuse(q,viewEnvironment.rgb);
#endif
    return viewSolar.rgb*(0.9*visibility.x*phases.x)+viewLunar.rgb*(0.9*visibility.y*phases.y)+diffuse;
}
/** Transports the representative physical source through atmosphere or local underwater medium exactly once. */
vec3 ViewTransport(vec3 source,vec3 displacement)
{
    if(viewAerial.z<=.7) return VgeApplyAerial(source,displacement,1.0,viewAerial.xyw,viewSun.xyz);
    float depth=length(displacement),clamped=min(depth,250.0);
    float extra=max(-viewFog.z*clamped*viewFog.w/60.0,0.0);
    float fog=max(1.0-exp(-(displacement.y-viewFog.w)*viewFog.z),1.0-exp(-clamped*viewFog.x-extra));
    fog=max(min(.04,fog),fog-clamp((8.0-depth)/8.0,0.0,.9));
    return mix(source,VgeCompatibilityLuminance(viewFogColor.rgb),clamp(fog+viewFog.y,0.0,1.0));
}
/** Preserves native interval-near-depth bucket weights independently of midpoint density evaluation. */
vec3 ViewBinWeights(float distance)
{
    float adjusted=distance>500.0?distance-250.0:distance*distance*distance*(1.0-distance/1000.0)/250000.0;
    float bin=log(1.0+adjusted/30.0);
    return vec3(vgeOitBellCurve(bin),vgeOitBellCurve(bin-1.0),bin>2.0?1.0:vgeOitBellCurve(bin-2.0));
}
/** Integrates a deterministic finite layer lattice and emits the six native physical/OIT products. */
void main()
{
    if(!VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration())) discard;
    vec2 uv=gl_FragCoord.xy/vgeFrame.screenSize,ndc=uv*2.0-1.0;
    vec3 direction=normalize(ViewUnproject(ndc,1.0));
    float depth=min(textureLod(viewMaterialDepth,uv,0).r,textureLod(viewVisibilityDepth,uv,0).r);
    float limit=min(length(ViewUnproject(ndc,depth)),length(ViewUnproject(ndc,textureLod(viewLiquidDepth,uv,0).r)));
    vec3 origin=-candidateOffsetWidth.xyz;origin.xz+=candidateOffsetWidth.w*25.0;
    vec3 bins=vec3(1),radiance=vec3(0);float transmission=1.0,weightSum=0.0,moment=0.0;
    vec2 span;
    if(ViewSpan(origin,direction,limit,span))
    {
        float lengthWorld=span.y-span.x;
        int count=min(int(viewBudget.x),max(1,int(floor(viewBudget.x*min(1.0,lengthWorld/viewBudget.y)))));
        float ds=lengthWorld/float(count),dsMetres=ds*candidateAnchorUnits.z;
        vec2 phases=vec2(ViewPhase(direction,viewSun.xyz),ViewPhase(direction,viewMoon.xyz));
        CandidateDensityCache cache=CandidateDensityCacheStart();
        for(int i=0;i<96;i++)
        {
            if(i>=count) break;
            float near=span.x+float(i)*ds,t=span.x+lengthWorld*(float(i)+0.5)/float(count);
            vec3 q=origin+direction*t;ivec2 cell=ivec2(floor(q.xz/50.0));
            vec4 column=CandidateDensityColumn(cell,cache);float sigma=CandidateDensity(q,cell,column,cache);
            float tau=dsMetres>0.0?min(sigma,1e30/dsMetres)*dsMetres:0.0;
            float alpha=clamp(1.0-exp(-min(tau,1e30)),0.0,1.0);
            if(alpha<=0.0) continue;
            float weight=transmission*alpha;
            if(weight>0.0) {radiance+=weight*ViewSource(q,phases);weightSum+=weight;moment+=weight*t;}
            transmission*=1.0-alpha;
            bins*=clamp(vec3(1)-alpha*ViewBinWeights(near),0.0,1.0);
#if VGE_LIGHTING_VOLUME_TERMINATION
            if(transmission<viewBudget.z) break;
#endif
        }
    }
    // Normalize before the affine medium response, then encode once and restore opacity weighting.
    if(weightSum>0.0) radiance=VgeStoreSceneLighting(ViewTransport(radiance/weightSum,direction*(moment/weightSum)))*weightSum;
    float opacity=clamp(1.0-transmission,0.0,1.0);vec4 k=vec4(radiance,opacity);
    outOitRevealBins=clamp(bins,0.0,1.0);outRevealage=clamp(transmission,0.0,1.0);outOitGlow=vec4(0,0,0,opacity);
    outOitAccumulation0=k*(1.0-bins.x);outOitAccumulation1=k*(1.0-bins.y);outOitAccumulation2=k*(1.0-bins.z);
}
