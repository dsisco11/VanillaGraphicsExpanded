#ifndef VGE_CLOUD_VOLUME_PRIMARY
#define VGE_CLOUD_VOLUME_PRIMARY
#ifndef VGE_CLOUD_PRIMARY_LIMIT
#define VGE_CLOUD_PRIMARY_LIMIT 384
#endif
/** Clips the finite horizontal domain using exact-zero parallel semantics, independently of density. */
bool CloudPrimarySpan(vec3 origin,vec3 direction,float limit,out vec2 span)
{
    float near=0.0,far=limit,extent=volumeOffsetWidth.w*50.0;
    span=vec2(0);
    for(int axis=0;axis<2;axis++)
    {
        float o=origin.xz[axis],d=direction.xz[axis];
        if(d==0.0) { if(o<0.0||o>extent) return false; }
        else
        {
            vec2 hits=vec2(-o,extent-o)/d;
            near=max(near,min(hits.x,hits.y));far=min(far,max(hits.x,hits.y));
        }
    }
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
