#version 430 core

// Phase 22.6: Feedback-driven residency gather (v1)
// Reads PatchIdGBuffer (RGBA32UI written as uvec4) and appends page requests.
//
// IMPORTANT:
// The v0 approach appended *every pixel* and relied on vge_maxRequests as an overflow clamp.
// That produced extremely biased request streams (first N writes tended to come from the same screen region),
// causing starvation where "new allocations never happen".
//
// v1 approach: do a bounded, deterministic sample of the PatchIdGBuffer.
// Dispatch exactly vge_sampleCount threads and sample pseudo-random pixels across the screen.
// This keeps request diversity high even when the screen contains millions of eligible pixels.

layout(local_size_x = 256, local_size_y = 1, local_size_z = 1) in;

// PatchIdGBuffer: (chunkSlot, patchId, packedPatchUv, misc/flags)
layout(binding = 0) uniform usampler2D vge_patchIdGBuffer;

layout(binding = 0, offset = 0) uniform atomic_uint vge_pageRequestCount;

// Each request is (chunkSlot, virtualPageIndex, mip, flags)
layout(std430, binding = 0) buffer VgePageRequests
{
    uvec4 vge_pageRequests[];
};

@import "./includes/lumonscene_feedback_gather_params_ubo.glsl"

uint vge_maxRequests() { return vgeFeedbackGatherParams.u0.x; }
uint vge_frameIndex() { return vgeFeedbackGatherParams.u0.y; }
uvec2 vge_screenSize() { return vgeFeedbackGatherParams.u1.xy; }
uint vge_sampleCount() { return vgeFeedbackGatherParams.u0.z; }

void main()
{
    uint sampleIndex = gl_GlobalInvocationID.x;
    if (sampleIndex >= vge_sampleCount())
    {
        return;
    }

    uvec2 ss = vge_screenSize();
    uint w = max(1u, ss.x);
    uint h = max(1u, ss.y);
    uint pixelCount = w * h;

    // Deterministic permutation-ish mapping (good distribution; exact bijection for power-of-two pixelCount).
    // Keep constants odd so power-of-two domains have full period.
    uint pixelLinear = (sampleIndex * 747796405u + vge_frameIndex() * 2891336453u) % pixelCount;
    ivec2 p = ivec2(int(pixelLinear % w), int(pixelLinear / w));

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
    if (idx >= vge_maxRequests())
    {
        return;
    }

    // Note: v1 also includes the original patchId in .w for CPU scheduling/debug.
    vge_pageRequests[idx] = uvec4(chunkSlot, virtualPageIndex, 0u, patchId);
}
