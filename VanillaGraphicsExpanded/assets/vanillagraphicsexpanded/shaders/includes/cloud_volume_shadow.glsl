@import "./cloud_volume_parameters.glsl"
@import "./cloud_volume_sampling.glsl"
@import "./cloud_volume_density.glsl"
@import "./cloud_volume_primary.glsl"
@import "./cloud_volume_hierarchy.glsl"
#if VGE_CLOUD_SELF_SHADOWING
#define CloudDirectional CloudMarchDirectional
@import "./cloud_volume_light.glsl"
#undef CloudDirectional
@import "./cloud_shadow_frame.glsl"
@import "./cloud_shadow_filter.glsl"
uniform sampler2D cloudShadowCache;
vec3 cloudCacheCenter,cloudCacheU,cloudCacheV,cloudCacheRadius,cloudCachedLight;
int cloudCacheSource;
bool cloudCacheFrameValid;
/** Prepares source selection and the projection once per primary ray, outside its lighting loop. */
void CloudPrepareShadowCache()
{
    cloudCacheSource=cloudTraversal.z-1;
    cloudCachedLight=cloudCacheSource==0?volumeSun.xyz:volumeMoon.xyz;
    cloudCacheFrameValid=cloudTraversal.z>0 && CloudShadowFrame(cloudCacheSource,cloudCachedLight,cloudCacheCenter,cloudCacheU,cloudCacheV,cloudCacheRadius);
}
/** Filters each ray's finite source window independently; uncertain footprint coverage falls back. */
bool CloudCachedTransmission(vec3 point,vec3 light,out vec2 transmission)
{
    transmission=vec2(0);
    if(!cloudCacheFrameValid||any(notEqual(light,cloudCachedLight))) return false;
    vec3 relative=point-cloudCacheCenter;
    vec2 uv=vec2(dot(relative,cloudCacheU),dot(relative,cloudCacheV))/(2.0*cloudCacheRadius.xy)+.5;
    if(any(lessThan(uv,vec2(0)))||any(greaterThanEqual(uv,vec2(1)))) return false;
    float depth=cloudCacheRadius.z-dot(relative,light);
    return CloudShadowTransmission(cloudShadowCache,uv,cloudCacheSource,depth,volumePrimary.z,
        volumeSamples.w,transmission);
}
/** Replaces only the cached source's secondary integration; all other transport uses production code. */
vec3 CloudDirectional(vec3 local,vec3 light,vec3 illuminance,float phase)
{
    if(max(illuminance.r,max(illuminance.g,illuminance.b))<=0.0) return vec3(0);
    vec2 transmission;
    if(!CloudCachedTransmission(local,light,transmission)) return CloudMarchDirectional(local,light,illuminance,phase);
    return illuminance*(phase*transmission.x+volumeSamples.z/12.5663706144*transmission.y);
}
#else
@import "./cloud_volume_light.glsl"
#endif
