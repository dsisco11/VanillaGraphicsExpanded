#ifndef VGE_CLOUD_VOLUME_LIGHT
#define VGE_CLOUD_VOLUME_LIGHT
#ifndef VGE_CLOUD_LIGHT_LIMIT
#define VGE_CLOUD_LIGHT_LIMIT 64
#endif
@import "./cloud_volume_primary.glsl"
/** Bounds shadow work independently of the camera while preserving its origin-anchored schedule. */
#if VGE_CLOUD_SELF_SHADOWING
bool CloudShadowSpan(vec3 origin,vec3 direction,out vec2 span)
{
    if(!CloudPrimarySpan(origin,direction,volumePrimary.z,span)) return false;
#ifdef VGE_CLOUD_HIERARCHY
    return CloudRootSpan(origin,direction,span);
#endif
    return true;
}
#endif
/** Integrates deterministic midpoints of quadratic intervals without per-column allocation or saturation. */
float CloudLightDepth(vec3 local,vec3 direction)
{
#if VGE_CLOUD_SELF_SHADOWING
    vec2 span;
    if(!CloudShadowSpan(local,direction,span)) return 0.0;
    float tau=0.0,distance=volumePrimary.z;
    int count=int(volumeSamples.y);
    CloudDensityCache densityCache=CloudDensityCacheStart();
    for(int i=0;i<VGE_CLOUD_LIGHT_LIMIT;i++)
    {
        if(i>=count) break;
        float a=float(i)/float(count),b=float(i+1)/float(count);
        float near=distance*a*a,far=distance*b*b;
        float position=(near+far)*.5;
        // Domain rejection never rescales the distance, shifts samples or reallocates their weights.
        if(position>span.y) break;
        if(position<span.x) continue;
        vec3 point=local+direction*position;
        ivec2 cell=ivec2(floor(point.xz/50.0));
        vec4 data=CloudDensityColumn(cell,densityCache);
        if(data.r<=0.0 || point.y<data.b*50.0 || point.y>data.a*50.0) continue;
        tau=min(1e30,tau+CloudDensity(point,cell,data,densityCache)*((far-near)*volumeAnchorUnits.z));
    }
    // Finite accumulation keeps the unoccluded octave endpoint well-defined at arbitrarily large tau.
    return tau;
#else
    return 0.0;
#endif
}
/** Evaluates the fixed anisotropic phase once for one primary ray and directional light. */
float CloudDirectionalPhase(vec3 view,vec3 light)
{
    float mu=clamp(dot(view,light),-1.0,1.0);
    return .64/(12.5663706144*pow(1.36-1.2*mu,1.5));
}
/** Selects unoccluded lighting for disabled shadows or a background-negligible directional source. */
bool CloudDirectionalUnshadowed(vec3 illuminance,float phase)
{
#if VGE_CLOUD_SELF_SHADOWING
    // Shadowing can only reduce this response. Retain the unoccluded light when its entire
    // contribution is below 0.001% of the environment in every channel, avoiding a shadow
    // march for a negligible source. Strict comparison keeps zero-background channels and
    // meaningful night lighting on the full integration path; no sample budget is changed.
    return all(lessThan(illuminance*(phase+volumeSamples.z/12.5663706144),volumeEnvironment.rgb*1e-5));
#else
    return true;
#endif
}
/** Combines sample-local shadow depth with a ray's fixed phase and one isotropic approximate octave. */
vec3 CloudDirectional(vec3 local,vec3 light,vec3 illuminance,float phase)
{
    if(max(illuminance.r,max(illuminance.g,illuminance.b))<=0.0) return vec3(0);
    float isotropic=volumeSamples.z/12.5663706144;
#if VGE_CLOUD_SELF_SHADOWING
    float tau=CloudLightDepth(local,light);
    return illuminance*(phase*exp(-tau)+isotropic*exp(-volumeSamples.w*tau));
#else
    return illuminance*(phase+isotropic);
#endif
}
#endif
