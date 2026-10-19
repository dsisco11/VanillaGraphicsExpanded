#ifndef VGE_STORED_CLOUD_ENCODING
#define VGE_STORED_CLOUD_ENCODING
/** Quantizes a bounded nonnegative component downward, including half subnormals. */
float CloudVolumeHalfDown(float value)
{
    uint bits=packHalf2x16(vec2(value,0))&65535u;
    float rounded=unpackHalf2x16(bits).x;
    return rounded<=value?rounded:unpackHalf2x16(bits-1u).x;
}
/** Bounds physical RGB before storing exactly representable half values; alpha is unused zero. */
vec4 CloudVolumeEncode(vec3 physical,vec3 cap,float scale)
{
    vec3 v=clamp(physical,vec3(0),cap)/scale;
    return vec4(CloudVolumeHalfDown(v.x),CloudVolumeHalfDown(v.y),CloudVolumeHalfDown(v.z),0);
}
#endif
