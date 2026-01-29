#version 430 core

// Phase 22.6: Feedback-driven residency gather (v1)
// Reads PatchIdGBuffer (RGBA32UI written as uvec4) and appends page requests.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// PatchIdGBuffer: (chunkSlot, patchId, packedPatchUv, misc/flags)
uniform usampler2D vge_patchIdGBuffer;

layout(binding = 0, offset = 0) uniform atomic_uint vge_pageRequestCount;

// Each request is (chunkSlot, virtualPageIndex, mip, flags)
layout(std430, binding = 0) buffer VgePageRequests
{
    uvec4 vge_pageRequests[];
};

uniform uint vge_maxRequests;

void main()
{
    ivec2 p = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = textureSize(vge_patchIdGBuffer, 0);
    if (p.x >= size.x || p.y >= size.y)
    {
        return;
    }

    uvec4 pid = texelFetch(vge_patchIdGBuffer, p, 0);
    uint chunkSlot = pid.x;
    uint patchId = pid.y;
    if (patchId == 0u)
    {
        return;
    }

    // v1: 1 page per patch, and virtual page index is derived from patchId.
    uint virtualPageIndex = patchId % uint(128u * 128u);

    uint idx = atomicCounterIncrement(vge_pageRequestCount);
    if (idx >= vge_maxRequests)
    {
        return;
    }

    // Note: v1 also includes the original patchId in .w for CPU scheduling/debug.
    vge_pageRequests[idx] = uvec4(chunkSlot, virtualPageIndex, 0u, patchId);
}
