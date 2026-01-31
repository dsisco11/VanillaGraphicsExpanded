#version 430 core

// Phase 22.6: Feedback-driven residency (v2)
// Mark visible virtual pages from PatchIdGBuffer into a deduplication stamp texture.
//
// This avoids biased append-buffer overflow behavior by replacing "append per pixel"
// with a visibility-driven "mark page used" step.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// PatchIdGBuffer: (chunkSlot, patchId, packedPatchUv, misc/flags)
uniform usampler2D vge_patchIdGBuffer;

// Virtual page usage stamp (R32UI). Texel contains the last frame stamp that touched it.
layout(binding = 0, r32ui) uniform uimage2D vge_pageUsageStamp;

// Current frame stamp (must be non-zero; monotonically increasing is fine).
uniform uint vge_frameStamp;

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

    // v1/v2: only chunkSlot==0 supported end-to-end.
    if (chunkSlot != 0u)
    {
        return;
    }

    uint virtualPageIndex = patchId % uint(128u * 128u);
    ivec2 vtexel = ivec2(int(virtualPageIndex & 127u), int(virtualPageIndex >> 7u));

    // Use max so repeated writes are idempotent and we don't need a clear pass.
    imageAtomicMax(vge_pageUsageStamp, vtexel, vge_frameStamp);
}

