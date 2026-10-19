#ifndef VGE_CLOUD_VOLUME_HIERARCHY
#define VGE_CLOUD_VOLUME_HIERARCHY
uniform sampler2D cloudHeightBounds;
layout(std140) uniform CloudTraversalParameters { ivec4 cloudTraversal; };

/** Clips only with a certified, correctly shaped root; unavailable summaries retain direct sampling. */
bool CloudRootSpan(vec3 origin,vec3 direction,inout vec2 span)
{
    int root=cloudTraversal.y;
    if(cloudTraversal.x==0 || root<0 || root>7) return true;
    if(volumeOffsetWidth.w<=float(1<<root) || volumeOffsetWidth.w>float(2<<root)
        || any(notEqual(textureSize(cloudHeightBounds,0),ivec2(1<<root)))) return true;
    vec2 bounds=texelFetch(cloudHeightBounds,ivec2(0),root).rg;
    if(bounds.x>bounds.y) return false;
    // Overflowed native heights cannot safely narrow a finite schedule. Keep the native queries.
    if(any(isnan(bounds)) || any(isinf(bounds))) return true;
    // Outward slack covers binary32 point formation and slab division, including near-parallel
    // rays. It depends on absolute coordinates and span, never an accumulated grid-crossing clock.
    float magnitude=max(1.0,max(max(abs(bounds.x),abs(bounds.y)),abs(origin.y)+abs(direction.y)*span.y));
    float slack=8.0*1.1920928955078125e-7*magnitude;
    // Small nonzero slopes still define entry/exit over a finite ray. Only exact
    // parallelism keeps the complete horizontal interval when inside the layer.
    if(direction.y==0.0) return origin.y>=bounds.x-slack&&origin.y<=bounds.y+slack;
    float near=span.x,far=span.y;
    vec2 hits=(bounds+vec2(-slack,slack)-origin.y)/direction.y;
    near=max(near,min(hits.x,hits.y));far=min(far,max(hits.x,hits.y));
    if(far<=near) return false;
    span=vec2(near,far);
    return true;
}
#endif
