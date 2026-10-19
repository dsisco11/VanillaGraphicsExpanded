@import "./cloud_volume_parameters.glsl"
@import "./cloud_volume_sampling.glsl"
@import "./cloud_volume_density.glsl"
@import "./cloud_volume_primary.glsl"
@import "./cloud_volume_hierarchy.glsl"
#define CloudDirectional CloudMarchDirectional
@import "./cloud_volume_light.glsl"
#undef CloudDirectional
@import "./cloud_shadow_frame.glsl"
uniform sampler2D cloudShadowCache;
vec3 cloudCacheCenter,cloudCacheU,cloudCacheV,cloudCacheRadius,cloudCachedLight;
int cloudCacheSource;
bool cloudCacheFrameValid;
/** Prepares source selection and the projection once per primary ray, outside its lighting loop. */
void CloudPrepareShadowCache()
{
    cloudCacheSource=cloudTraversal.z-1;
    cloudCachedLight=cloudCacheSource==0?volumeSun.xyz:volumeMoon.xyz;
    cloudCacheFrameValid=cloudTraversal.z>0 && CloudShadowFrame(cloudCachedLight,cloudCacheCenter,cloudCacheU,cloudCacheV,cloudCacheRadius);
}
/** Samples a mean-extinction approximation over the finite source distance; invalid coverage falls back. */
bool CloudCachedDepth(vec3 point,vec3 light,out float tau)
{
    tau=0.0;
    if(!cloudCacheFrameValid||any(notEqual(light,cloudCachedLight))) return false;
    vec3 relative=point-cloudCacheCenter;
    vec2 uv=vec2(dot(relative,cloudCacheU),dot(relative,cloudCacheV))/(2.0*cloudCacheRadius.xy)+.5;
    if(any(lessThan(uv,vec2(0)))||any(greaterThanEqual(uv,vec2(1)))) return false;
    vec4 record=textureLod(cloudShadowCache,uv,0.0);
    if(any(isnan(record))||any(isinf(record))||record.z<0.0) return false;
    if(record.w!=float(cloudCacheSource+1)) return false;
    if(record.y<=record.x) return true;
    float depth=cloudCacheRadius.z-dot(relative,light);
    // Compressing separated matter into one interval is intentional here. It preserves the
    // integrated map-ray optical depth, but changes its distribution and quantizes adjacent rays.
    float overlap=max(0.0,min(depth,record.y)-max(depth-volumePrimary.z,record.x));
    tau=record.z*(overlap/(record.y-record.x));
    return true;
}
/** Replaces only the cached source's secondary integration; all other transport uses production code. */
vec3 CloudDirectional(vec3 local,vec3 light,vec3 illuminance,float phase)
{
    if(max(illuminance.r,max(illuminance.g,illuminance.b))<=0.0) return vec3(0);
    float tau;
    if(!CloudCachedDepth(local,light,tau)) return CloudMarchDirectional(local,light,illuminance,phase);
    return illuminance*(phase*exp(-tau)+volumeSamples.z/12.5663706144*exp(-volumeSamples.w*tau));
}
