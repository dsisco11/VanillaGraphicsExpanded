#ifndef VGE_CLOUD_SHADOW_RECORD
#define VGE_CLOUD_SHADOW_RECORD
/** Evaluates a cumulative mean-extinction ramp in world depth, with extinction in inverse metres. */
float CloudShadowCumulative(vec3 record,float depth)
{
    return min(record.z,record.y*(max(0.0,depth-record.x)*volumeAnchorUnits.z));
}
/** Evaluates a compact record; source and publication are certified by the whole-cache owner. */
bool CloudShadowRecordDepth(vec3 record,float depth,float distance,out float tau)
{
    tau=0.0;
    if(isnan(depth)||isinf(depth)||isnan(distance)||isinf(distance)||distance<0.0) return false;
    if(any(isnan(record))||any(isinf(record))||any(lessThan(record,vec3(0)))) return false;
    // Both endpoints use the same fetched ramp; saturation represents its finite back depth.
    tau=max(0.0,CloudShadowCumulative(record,depth)-CloudShadowCumulative(record,depth-distance));
    return true;
}
/** Evaluates the direct and additional-octave responses from one finite-window optical depth. */
bool CloudShadowTransmission(sampler2D cache,vec2 uv,float depth,float distance,
    float octaveExtinction,out vec2 transmission)
{
    transmission=vec2(0);
    if(any(isnan(uv))||any(isinf(uv))||any(lessThan(uv,vec2(0)))||any(greaterThanEqual(uv,vec2(1)))
        ||isnan(octaveExtinction)||isinf(octaveExtinction)||octaveExtinction<0.0) return false;
    ivec2 size=textureSize(cache,0);
    vec2 edge=.5/vec2(size);
    // Only complete generated footprints are admitted. Clamp-to-edge sampler state
    // never authorizes extending border rays beyond their texel centers.
    if(size.x!=size.y||any(lessThan(uv,edge))||any(greaterThan(uv,vec2(1)-edge))) return false;
    vec3 record=textureLod(cache,uv,0.0).rgb;
    float tau;
    if(!CloudShadowRecordDepth(record,depth,distance,tau)) return false;
    transmission=exp(-vec2(tau,octaveExtinction*tau));return true;
}
#endif
