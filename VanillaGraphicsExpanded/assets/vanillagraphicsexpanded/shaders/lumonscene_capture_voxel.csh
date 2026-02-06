#version 430 core

// Phase 22.7: Voxel patch capture v1 (fast)
// For each CaptureWork item, fills the corresponding physical tile in:
// - DepthAtlas (r16f): planar depth = 0
// - MaterialAtlas (rgba8): RG = oct-encoded normal, BA = 16-bit surfaceId (derived from PBR registry via TraceScene palette)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, r16f) writeonly uniform image2DArray vge_depthAtlas;
layout(binding = 1, rgba8) writeonly uniform image2DArray vge_materialAtlas;

// TraceScene sampling inputs (L0 only in v1).
layout(binding = 2) uniform usampler3D vge_occL0;
layout(binding = 3) uniform usampler2D vge_materialPalette; // RGBA32UI (packs 6x 16-bit surfaceIds: faces 0..5)

@import "./includes/lumonscene_capture_voxel_params_ubo.glsl"

#define vge_occOriginMinCell0 (vgeCaptureVoxelParams.occOriginMinCell0.xyz)
#define vge_occRing0          (vgeCaptureVoxelParams.occRing0.xyz)
#define vge_occResolution     (vgeCaptureVoxelParams.occInts0.x)

layout(std430, binding = 0) buffer VgeCaptureWork
{
    uvec4 vge_captureWork[]; // (physicalPageId, chunkSlot, patchId, virtualPageIndex)
};

struct VgePatchMeta
{
    vec4 OriginWS;
    vec4 AxisUWS;
    vec4 AxisVWS;
    vec4 NormalWS;

    // Virtual-space placement for this patch/page.
    uint VirtualBasePageX;
    uint VirtualBasePageY;
    uint VirtualSizePagesX;
    uint VirtualSizePagesY;

    // Identity/debug / reverse mapping.
    uint ChunkSlot;
    uint PatchId;

    uint Reserved0;
    uint Reserved1;
};

layout(std430, binding = 1) buffer VgePatchMetadata
{
    VgePatchMeta vge_patchMeta[];
};

layout(std430, binding = 2) readonly buffer VgeChunkSlotInfo
{
    ivec4 vge_chunkOriginBlocksAndGeneration[]; // (originX, originY, originZ, generation)
};

#define vge_tileSizeTexels (vgeCaptureVoxelParams.atlasLayout.x)
#define vge_tilesPerAxis   (vgeCaptureVoxelParams.atlasLayout.y)
#define vge_tilesPerAtlas  (vgeCaptureVoxelParams.atlasLayout.z)
#define vge_borderTexels   (vgeCaptureVoxelParams.atlasLayout.w)

@import "./includes/lumonscene_trace_scene_occupancy.glsl"
@import "./includes/lumonscene_material_packing.glsl"

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

bool DecodeVoxelPatchId(uint patchId, out uint axisId, out uint planeIndex, out uint patchU, out uint patchV)
{
    axisId = 0u;
    planeIndex = 0u;
    patchU = 0u;
    patchV = 0u;

    if (patchId == 0u)
    {
        return false;
    }

    uint v = patchId - 1u;
    axisId = v % 6u;
    uint patchLinear = v / 6u;

    planeIndex = patchLinear / 64u;             // 0..31
    uint tile = patchLinear - planeIndex * 64u; // 0..63
    patchU = tile % 8u;                         // 0..7
    patchV = tile / 8u;                         // 0..7

    return planeIndex < 32u;
}

void ComputeVoxelPatchBasisAndOrigin(
    ivec3 chunkOriginBlocks,
    uint axisId,
    uint planeIndex,
    uint patchU,
    uint patchV,
    out vec3 originWS,
    out vec3 axisUWS,
    out vec3 axisVWS,
    out vec3 normalWS)
{
    // Voxel patch size is fixed in v1: 4x4 voxels == 4x4 blocks on a face.
    const float PatchSizeBlocks = 4.0;

    int u0 = int(patchU) * 4;
    int v0 = int(patchV) * 4;
    int p = int(planeIndex);

    if (axisId == 0u) // +X
    {
        originWS = vec3(chunkOriginBlocks + ivec3(p + 1, v0, u0));
        axisUWS = vec3(0.0, 0.0, PatchSizeBlocks);
        axisVWS = vec3(0.0, PatchSizeBlocks, 0.0);
        normalWS = vec3(1.0, 0.0, 0.0);
    }
    else if (axisId == 1u) // -X
    {
        originWS = vec3(chunkOriginBlocks + ivec3(p, v0, u0));
        axisUWS = vec3(0.0, 0.0, PatchSizeBlocks);
        axisVWS = vec3(0.0, PatchSizeBlocks, 0.0);
        normalWS = vec3(-1.0, 0.0, 0.0);
    }
    else if (axisId == 2u) // +Y
    {
        originWS = vec3(chunkOriginBlocks + ivec3(u0, p + 1, v0));
        axisUWS = vec3(PatchSizeBlocks, 0.0, 0.0);
        axisVWS = vec3(0.0, 0.0, PatchSizeBlocks);
        normalWS = vec3(0.0, 1.0, 0.0);
    }
    else if (axisId == 3u) // -Y
    {
        originWS = vec3(chunkOriginBlocks + ivec3(u0, p, v0));
        axisUWS = vec3(PatchSizeBlocks, 0.0, 0.0);
        axisVWS = vec3(0.0, 0.0, PatchSizeBlocks);
        normalWS = vec3(0.0, -1.0, 0.0);
    }
    else if (axisId == 4u) // +Z
    {
        originWS = vec3(chunkOriginBlocks + ivec3(u0, v0, p + 1));
        axisUWS = vec3(PatchSizeBlocks, 0.0, 0.0);
        axisVWS = vec3(0.0, PatchSizeBlocks, 0.0);
        normalWS = vec3(0.0, 0.0, 1.0);
    }
    else // -Z
    {
        originWS = vec3(chunkOriginBlocks + ivec3(u0, v0, p));
        axisUWS = vec3(PatchSizeBlocks, 0.0, 0.0);
        axisVWS = vec3(0.0, PatchSizeBlocks, 0.0);
        normalWS = vec3(0.0, 0.0, -1.0);
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
    uint chunkSlot = w.y;
    uint patchId = w.z;
    uint virtualPageIndex = w.w;

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

    // Material: packed normal + surfaceId (resolved from TraceScene material palette).
    vec3 normalWS = NormalFromPatchId(patchId);
    vec4 outMat = VgeLumonScenePackMaterialAtlas(normalWS, 0u);
    if (vge_occResolution > 0)
    {
        ivec4 og = vge_chunkOriginBlocksAndGeneration[chunkSlot];
        ivec3 chunkOrigin = og.xyz;

        uint axisId, planeIndex, patchU, patchV;
        vec3 originWS, axisUWS, axisVWS, patchNormalWS;

        if (!DecodeVoxelPatchId(patchId, axisId, planeIndex, patchU, patchV))
        {
            originWS = vec3(chunkOrigin);
            axisUWS = vec3(0.0);
            axisVWS = vec3(0.0);
            patchNormalWS = vec3(0.0, 1.0, 0.0);
        }
        else
        {
            ComputeVoxelPatchBasisAndOrigin(chunkOrigin, axisId, planeIndex, patchU, patchV, originWS, axisUWS, axisVWS, patchNormalWS);
        }

        normalWS = patchNormalWS;

        const uint PatchSizeVoxels = 4u;
        uint faceTexels = max(1u, vge_tileSizeTexels / PatchSizeVoxels);

        uint cellU = min(PatchSizeVoxels - 1u, inTile.x / faceTexels);
        uint cellV = min(PatchSizeVoxels - 1u, inTile.y / faceTexels);

        uint inU = inTile.x - cellU * faceTexels;
        uint inV = inTile.y - cellV * faceTexels;

        float fu = (float(inU) + 0.5) / float(faceTexels);
        float fv = (float(inV) + 0.5) / float(faceTexels);

        vec3 stepU = axisUWS * (1.0 / float(PatchSizeVoxels));
        vec3 stepV = axisVWS * (1.0 / float(PatchSizeVoxels));

        vec3 surfacePos = originWS + stepU * (float(cellU) + fu) + stepV * (float(cellV) + fv);
        ivec3 sampleCell = ivec3(floor(surfacePos - patchNormalWS * 0.01));

        uint payloadPacked = VgeSampleOccL0(vge_occL0, sampleCell, vge_occOriginMinCell0, vge_occRing0, vge_occResolution);
        uint matIndex = (payloadPacked >> 18u) & 16383u;
        if (matIndex != 0u)
        {
            uvec4 faces = texelFetch(vge_materialPalette, ivec2(int(matIndex), 0), 0);
            uint faceIndex = VgeLumonSceneAxisIdToBlockFaceIndex(axisId);
            uint surfaceId = VgeLumonSceneUnpackFaceSurfaceId(faces, faceIndex);
            outMat = VgeLumonScenePackMaterialAtlas(patchNormalWS, surfaceId);
        }
    }

    imageStore(vge_materialAtlas, texel, outMat);

    // Write patch metadata once per work item (avoid per-texel SSBO traffic).
    if (inTile.x == 0u && inTile.y == 0u)
    {
        ivec4 og = vge_chunkOriginBlocksAndGeneration[chunkSlot];
        ivec3 chunkOrigin = og.xyz;
        uint generation = uint(og.w) & 0xFFFFu;

        uint axisId, planeIndex, patchU, patchV;
        vec3 originWS, axisUWS, axisVWS, normalWS;

        if (!DecodeVoxelPatchId(patchId, axisId, planeIndex, patchU, patchV))
        {
            originWS = vec3(chunkOrigin);
            axisUWS = vec3(0.0);
            axisVWS = vec3(0.0);
            normalWS = vec3(0.0, 1.0, 0.0);
        }
        else
        {
            ComputeVoxelPatchBasisAndOrigin(chunkOrigin, axisId, planeIndex, patchU, patchV, originWS, axisUWS, axisVWS, normalWS);
        }

        uint vx = virtualPageIndex & 127u;
        uint vy = virtualPageIndex >> 7u;

        VgePatchMeta meta;
        meta.OriginWS = vec4(originWS, 0.0);
        meta.AxisUWS = vec4(axisUWS, 0.0);
        meta.AxisVWS = vec4(axisVWS, 0.0);
        meta.NormalWS = vec4(normalWS, 0.0);

        meta.VirtualBasePageX = vx;
        meta.VirtualBasePageY = vy;
        meta.VirtualSizePagesX = 1u;
        meta.VirtualSizePagesY = 1u;

        meta.ChunkSlot = chunkSlot;
        meta.PatchId = patchId;
        meta.Reserved0 = generation;
        meta.Reserved1 = 0u;

        vge_patchMeta[physicalPageId] = meta;
    }
}
