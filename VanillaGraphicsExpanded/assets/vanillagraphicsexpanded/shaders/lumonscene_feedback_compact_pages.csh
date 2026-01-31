#version 430 core

// Phase 22.6: Feedback-driven residency (v2)
// Compact the deduplicated virtual-page usage stamp texture into a bounded request list.

layout(local_size_x = 256, local_size_y = 1, local_size_z = 1) in;

// Virtual page usage stamp written by lumonscene_feedback_mark_pages.csh.
layout(binding = 0, r32ui) readonly uniform uimage2DArray vge_pageUsageStamp;

layout(binding = 0, offset = 0) uniform atomic_uint vge_pageRequestCount;

// Each request is (chunkSlot, virtualPageIndex, mip, flags/patchId)
layout(std430, binding = 0) buffer VgePageRequests
{
    uvec4 vge_pageRequests[];
};

uniform uint vge_maxRequests;
uniform uint vge_frameStamp;
uniform uint vge_scanOffset;

void main()
{
    ivec3 stampSize = imageSize(vge_pageUsageStamp);
    uint chunkSlotCount = uint(max(stampSize.z, 1));
    uint totalEntries = uint(128u * 128u) * chunkSlotCount;

    uint linear = gl_GlobalInvocationID.x;
    if (linear >= totalEntries)
    {
        return;
    }

    // Fairness: scan offset ensures no single chunkSlot permanently dominates the bounded request list.
    // The CPU increments vge_scanOffset each frame (typically by VirtualPagesPerChunk) to rotate slots.
    uint idx0 = (linear + vge_scanOffset) % totalEntries;

    uint virtualPageIndex = idx0 % uint(128u * 128u);
    uint chunkSlot = idx0 / uint(128u * 128u);

    ivec3 vtexel = ivec3(int(virtualPageIndex & 127u), int(virtualPageIndex >> 7u), int(chunkSlot));
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
    vge_pageRequests[idx] = uvec4(chunkSlot, virtualPageIndex, 0u, virtualPageIndex);
}
