#version 430 core

// Phase 22.6: Feedback-driven residency (v2)
// Compact the deduplicated virtual-page usage stamp texture into a bounded request list.

layout(local_size_x = 256, local_size_y = 1, local_size_z = 1) in;

// Virtual page usage stamp written by lumonscene_feedback_mark_pages.csh.
layout(binding = 0, r32ui) readonly uniform uimage2D vge_pageUsageStamp;

layout(binding = 0, offset = 0) uniform atomic_uint vge_pageRequestCount;

// Each request is (chunkSlot, virtualPageIndex, mip, flags/patchId)
layout(std430, binding = 0) buffer VgePageRequests
{
    uvec4 vge_pageRequests[];
};

uniform uint vge_maxRequests;
uniform uint vge_frameStamp;

void main()
{
    uint virtualPageIndex = gl_GlobalInvocationID.x;
    if (virtualPageIndex >= uint(128u * 128u))
    {
        return;
    }

    ivec2 vtexel = ivec2(int(virtualPageIndex & 127u), int(virtualPageIndex >> 7u));
    uint stamp = imageLoad(vge_pageUsageStamp, vtexel).x;
    if (stamp != vge_frameStamp)
    {
        return;
    }

    uint idx = atomicCounterIncrement(vge_pageRequestCount);
    if (idx >= vge_maxRequests)
    {
        return;
    }

    // v2: patchId is placeholder; for voxel patches patchId==virtualPageIndex (1..12288).
    vge_pageRequests[idx] = uvec4(0u, virtualPageIndex, 0u, virtualPageIndex);
}

