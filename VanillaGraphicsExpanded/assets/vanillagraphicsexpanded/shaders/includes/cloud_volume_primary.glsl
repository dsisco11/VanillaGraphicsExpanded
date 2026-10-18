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
#endif
