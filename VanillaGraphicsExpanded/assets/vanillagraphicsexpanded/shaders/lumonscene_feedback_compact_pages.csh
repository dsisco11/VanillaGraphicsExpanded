#version 430 core

// Phase 22.6: Feedback-driven residency (v2)
// Compact the deduplicated virtual-page usage stamp texture into a bounded request list.

layout(local_size_x = 256, local_size_y = 1, local_size_z = 1) in;

// Virtual page usage stamp written by lumonscene_feedback_mark_pages.csh.
layout(binding = 0) uniform usampler2DArray vge_pageUsageStamp;

// Near-field page table mip0 (R32UI packed entry) used to filter mapped vs unmapped virtual pages.
// Note: binding here refers to the texture unit, not the image/SSBO binding points.
layout(binding = 1) uniform usampler2DArray vge_pageTableMip0;

layout(binding = 0, offset = 0) uniform atomic_uint vge_pageRequestCount;

// Each request is (chunkSlot, virtualPageIndex, mip, flags/patchId)
layout(std430, binding = 0) buffer VgePageRequests
{
    uvec4 vge_pageRequests[];
};

uniform uint vge_maxRequests;
uniform uint vge_frameStamp;
uniform uint vge_scanOffset;
uniform uint vge_compactMode; // 0=emit mapped pages only, 1=emit unmapped pages only (runtime uses 1)

void main()
{
    ivec3 stampSize = textureSize(vge_pageUsageStamp, 0);
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
    uint stamp = texelFetch(vge_pageUsageStamp, vtexel, 0).x;
    if (stamp != vge_frameStamp)
    {
        return;
    }

    // Determine whether the virtual page is already mapped in the page table.
    // packedEntry.x: [0..23]=physicalPageId, [24..31]=flags
    uint packedEntry = texelFetch(vge_pageTableMip0, ivec3(vtexel), 0).x;
    bool mapped = (packedEntry & 0xFFFFFFu) != 0u;

    if (vge_compactMode == 0u)
    {
        // Emit only pages that already have a mapping (diagnostics / optional maintenance pass).
        if (!mapped) return;
    }
    else
    {
        // Emit only pages that are currently unmapped (allocation pass).
        if (mapped) return;
    }

    uint idx = atomicCounterIncrement(vge_pageRequestCount);
    if (idx >= vge_maxRequests)
    {
        return;
    }

    // v2: patchId is placeholder; for voxel patches patchId==virtualPageIndex (1..12288).
    vge_pageRequests[idx] = uvec4(chunkSlot, virtualPageIndex, 0u, virtualPageIndex);
}
