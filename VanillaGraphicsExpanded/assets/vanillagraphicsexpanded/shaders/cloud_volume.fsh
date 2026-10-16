#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/cloud_volume_parameters.glsl"
@import "./includes/cloud_volume_density.glsl"
@import "./includes/atmosphere_aerial.glsl"
@import "./includes/pbr_scene_storage.glsl"
@import "./includes/pbr_color.glsl"
@import "./includes/oit.glsl"
uniform sampler2D sceneDepth;
uniform sampler2D liquidDepth;

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
vec3 CloudDirectional(vec3 local,vec3 view,vec3 light,vec3 illuminance)
{
    if(max(illuminance.r,max(illuminance.g,illuminance.b))<=0.0) return vec3(0);
    float tau=CloudLightDepth(local,light), mu=clamp(dot(view,light),-1.0,1.0);
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
/** Integrates physical source front-to-back while retaining native per-interval bucket transmission. */
void main()
{
    if(!VgeFrameLightingValid(vgeFrame.frameIndex,VgeLightingCalibrationGeneration())) discard;
    vec2 uv=gl_FragCoord.xy/vgeFrame.screenSize, ndc=uv*2.0-1.0;
    vec3 direction=normalize(CloudUnproject(ndc,1.0));
    // Native clips at both opaque and liquid depth. Normalized UV handles quarter-size liquid storage.
    float limit=min(length(CloudUnproject(ndc,texture(sceneDepth,uv).r)),length(CloudUnproject(ndc,texture(liquidDepth,uv).r)));
    vec3 origin=-volumeOffsetWidth.xyz;
    origin.xz+=volumeOffsetWidth.w*25.0;
    CloudRay ray;
    if(!CloudStart(origin,direction,limit,ray)) discard;
    vec4 k=vec4(0); vec3 reveal=vec3(1); float transmission=1.0;
    for(int cellIndex=0;cellIndex<402;cellIndex++)
    {
        vec4 data=CloudColumn(ray.cell); float near,far;
        if(CloudInterval(ray,data,near,far))
        {
            int count=volumeDetail.w>.5?int(volumeSamples.x):1;
            float ds=(far-near)/float(count);
            for(int i=0;i<16;i++)
            {
                if(i>=count) break;
                float t=near+(float(i)+.5)*ds;
                vec3 local=ray.o+direction*t;
                float sigma=CloudDensity(local,ray.cell,data);
                float alpha=clamp(1.0-exp(-sigma*(ds*volumeAnchorUnits.z)),0.0,1.0);
                if(alpha<=0.0) continue;
                vec3 source=volumeEnvironment.rgb
                    +CloudDirectional(local,direction,volumeSun.xyz,volumeSolar.rgb)
                    +CloudDirectional(local,direction,volumeMoon.xyz,volumeLunar.rgb);
                source=CloudTransport(source,direction*t);
                // Encode only RGB, before view coverage and native bucket weighting.
                k.rgb+=transmission*alpha*VgeStoreSceneLighting(source);
                transmission*=1.0-alpha;
                float bin=log(CloudAdjustedDepth(near+float(i)*ds)/30.0+1.0);
                vec3 b=vec3(vgeOitBellCurve(bin),vgeOitBellCurve(bin-1.0),bin>2.0?1.0:vgeOitBellCurve(bin-2.0));
                reveal*=clamp(vec3(1)-alpha*b,0.0,1.0);
            }
        }
        if(transmission<1e-6||!CloudAdvance(ray)) break;
    }
    k.a=clamp(1.0-transmission,0.0,1.0);
    if(k.a<=0.0) discard;
    outOitRevealBins=clamp(reveal,0.0,1.0); outRevealage=clamp(transmission,0.0,1.0);
    outOitAccumulation0=k*(1.0-reveal.x); outOitAccumulation1=k*(1.0-reveal.y); outOitAccumulation2=k*(1.0-reveal.z);
    outOitGlow=vec4(0,0,0,k.a);
}
