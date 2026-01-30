#version 430 core

// Phase 22.7: Voxel patch capture v1 (fast)
// For each CaptureWork item, fills the corresponding physical tile in:
// - DepthAtlas (r16f): planar depth = 0
// - MaterialAtlas (rgba8): placeholder normal (axis) + placeholder material

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, r16f) writeonly uniform image2DArray vge_depthAtlas;
layout(binding = 1, rgba8) writeonly uniform image2DArray vge_materialAtlas;

layout(std430, binding = 0) buffer VgeCaptureWork
{
    uvec4 vge_captureWork[]; // (physicalPageId, chunkSlot, patchId, virtualPageIndex)
};

uniform uint vge_tileSizeTexels;
uniform uint vge_tilesPerAxis;
uniform uint vge_tilesPerAtlas;
uniform uint vge_borderTexels; // v1 default 0

vec3 NormalFromPatchId(uint patchId)
{
    if (patchId == 0u)
    {
        return vec3(0.0, 1.0, 0.0);
    }

    uint f = (patchId - 1u) % 6u;
    switch (f)
    {
        case 0u: return vec3( 1.0, 0.0, 0.0);
        case 1u: return vec3(-1.0, 0.0, 0.0);
        case 2u: return vec3( 0.0, 1.0, 0.0);
        case 3u: return vec3( 0.0,-1.0, 0.0);
        case 4u: return vec3( 0.0, 0.0, 1.0);
        default: return vec3( 0.0, 0.0,-1.0);
    }
}

void main()
{
    uvec3 gid = gl_GlobalInvocationID;
    uint workIndex = gid.z;

    uvec2 inTile = gid.xy;
    if (inTile.x >= vge_tileSizeTexels || inTile.y >= vge_tileSizeTexels)
    {
        return;
    }

    uvec4 w = vge_captureWork[workIndex];
    uint physicalPageId = w.x;
    uint patchId = w.z;

    if (physicalPageId == 0u)
    {
        return;
    }

    uint pageIndex = physicalPageId - 1u;
    uint atlasIndex = pageIndex / vge_tilesPerAtlas;
    uint local = pageIndex - atlasIndex * vge_tilesPerAtlas;
    uint tileY = local / vge_tilesPerAxis;
    uint tileX = local - tileY * vge_tilesPerAxis;

    ivec2 base = ivec2(int(tileX * vge_tileSizeTexels), int(tileY * vge_tileSizeTexels));
    ivec2 texelXY = base + ivec2(inTile);
    ivec3 texel = ivec3(texelXY, int(atlasIndex));

    // Border semantics (v1): no border, but keep the plumbing.
    // When borderTexels > 0, we would clamp sampling to the inner patch area.
    // Current v1 content is constant so this is a no-op.
    // Keep vge_borderTexels as a live uniform (avoid driver DCE) without changing behavior for sane values.
    if (vge_borderTexels == 0xFFFFFFFFu) { return; }

    // Depth: planar.
    imageStore(vge_depthAtlas, texel, vec4(0.0));

    // Material: placeholder axis normal in RGB, constant alpha.
    vec3 n = NormalFromPatchId(patchId);
    vec3 n01 = n * 0.5 + 0.5;
    imageStore(vge_materialAtlas, texel, vec4(n01, 1.0));
}
