#ifndef VGE_CLOUD_VOLUME_PRIMARY
#define VGE_CLOUD_VOLUME_PRIMARY
#ifndef VGE_CLOUD_PRIMARY_LIMIT
#define VGE_CLOUD_PRIMARY_LIMIT 384
#endif
/** Clips a complete primary span without reading density or changing it according to acceleration availability. */
bool CloudPrimarySpan(vec3 origin,vec3 direction,float limit,out vec2 span)
{
    float near=0.0,far=limit,extent=volumeOffsetWidth.w*50.0;
    span=vec2(0);
    if(!CloudSlab(origin.x,direction.x,0.0,extent,near,far)) return false;
    if(!CloudSlab(origin.z,direction.z,0.0,extent,near,far)) return false;
    span=vec2(near,far);
    return far>near;
}
/** Allocates a bounded sample count across the whole clipped distance, including empty gaps. */
int CloudPrimaryCount(float distance)
{
    return min(int(volumeSamples.x),max(int(volumePrimary.x),
        int(floor(volumeSamples.x*min(1.0,distance/volumePrimary.y)))));
}
/** Derives every position directly from the fixed lattice; skipped support cannot perturb later samples. */
float CloudPrimaryPosition(vec2 span,int count,int index,float jitter)
{
    return span.x+(float(index)+jitter)*((span.y-span.x)/float(count));
}
/** Bounds indices on the original lattice without reallocating samples to the supported interval. */
ivec2 CloudPrimaryRange(vec2 span,vec2 supported,int count,float jitter)
{
    if(supported.x<=span.x && supported.y>=span.y) return ivec2(0,count);
    float step=(span.y-span.x)/float(count);
    if(!(step>0.0) || isnan(step) || isinf(step)) return ivec2(0,count);
    vec2 indices=(supported-vec2(span.x))/step-vec2(jitter);
    if(any(isnan(indices)) || any(isinf(indices))) return ivec2(0,count);
    // Root bounds already admit coordinate-rounding uncertainty. Retain an additional lattice
    // point beyond floor/ceil at either end so inverse index arithmetic cannot discard a tie.
    // Every surviving point still uses its original index, position, ds and native OIT depth.
    return ivec2(clamp(floor(indices.x)-1.0,0.0,float(count)),
        clamp(ceil(indices.y)+2.0,0.0,float(count)));
}
#endif
