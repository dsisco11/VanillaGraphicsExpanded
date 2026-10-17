#ifndef VGE_CLOUD_VOLUME_HIERARCHY
#define VGE_CLOUD_VOLUME_HIERARCHY
uniform sampler2D cloudHeightBounds;
layout(std140) uniform CloudTraversalParameters { ivec4 cloudTraversal; };

/** Replays a node's exit clock instead of recomputing plane division or multiplying the DDA delta. */
float CloudNodeExit(CloudRay ray, int span)
{
    ivec2 lo=(ray.cell/span)*span;
    ivec2 hi=min(lo+ivec2(span),ivec2(int(volumeOffsetWidth.w)));
    float exit=ray.end;
    for(int axis=0;axis<2;axis++)
    {
        if(abs(ray.d.xz[axis])<1e-8) continue;
        int planes=ray.stepCell[axis]>0?hi[axis]-ray.cell[axis]:ray.cell[axis]-lo[axis]+1;
        float clock=ray.next[axis];
        for(int hop=1;hop<200;hop++)
        {
            if(hop>=planes) break;
            clock+=ray.delta[axis];
        }
        exit=min(exit,clock);
    }
    return exit;
}

/** Advances to a proven-missed node's exit with the original repeated-addition and tied-axis semantics. */
bool CloudSkipNode(inout CloudRay ray,float exit)
{
    if(exit>=ray.end) return false;
    if(!(exit>ray.t)) return CloudAdvance(ray);
    for(int axis=0;axis<2;axis++)
    for(int hop=0;hop<200;hop++)
    {
        if(ray.next[axis]>exit) break;
        ray.cell[axis]+=ray.stepCell[axis];
        ray.next[axis]+=ray.delta[axis];
    }
    ray.t=exit;
    return all(greaterThanEqual(ray.cell,ivec2(0))) && all(lessThan(ray.cell,ivec2(int(volumeOffsetWidth.w))));
}

/** Finds the next potentially occupied leaf without changing surviving native-column quadrature. */
bool CloudHierarchySeek(inout CloudRay ray)
{
    if(cloudTraversal.x==0) return true;
    int root=cloudTraversal.y;
    // Certificates are checked by the CPU owner. Reject malformed dimensions before any mip fetch.
    if(root<0 || root>7) return true;
    int leaves=2<<root;
    if(volumeOffsetWidth.w>float(leaves) || volumeOffsetWidth.w<=float(leaves/2)
        || any(notEqual(textureSize(cloudHeightBounds,0),ivec2(1<<root)))) return true;
    for(int region=0;region<402;region++)
    {
        bool skipped=false;
        for(int mip=root;mip>=0;mip--)
        {
            int span=2<<mip;
            vec2 bounds=texelFetch(cloudHeightBounds,ray.cell/span,mip).rg;
            // A node already covering the current ray parameter cannot prove a miss. Descend
            // without replaying exit clocks, keeping dense/inside-cloud paths inexpensive.
            float enter=ray.t,leave=ray.end;
            if(bounds.x<=bounds.y && CloudSlab(ray.o.y,ray.d.y,bounds.x,bounds.y,enter,leave)
                && enter<=ray.t) continue;
            float exit=CloudNodeExit(ray,span),near=ray.t,far=exit;
            if(bounds.x>bounds.y || !CloudSlab(ray.o.y,ray.d.y,bounds.x,bounds.y,near,far))
            {
                if(!CloudSkipNode(ray,exit)) return false;
                skipped=true;
                break;
            }
        }
        if(!skipped) return true;
    }
    // A defensive work limit falls back at the current cell; it must never discard remaining density.
    return true;
}
#endif
