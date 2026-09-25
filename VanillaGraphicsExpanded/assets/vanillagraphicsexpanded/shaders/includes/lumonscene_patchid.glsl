#ifndef VGE_LUMONSCENE_PATCHID_GLSL
#define VGE_LUMONSCENE_PATCHID_GLSL
// ============================================================================
// LumonScene PatchId encoding helpers
//
// v1: Deterministic voxel-patch mapping for chunk terrain.
// - Patch granularity: 4x4 voxels per patch.
// - PatchId is 1-based (0 means invalid).
// - (patchId - 1) % 6 encodes face axis:
//     0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
//
// PatchId is chunk-local (size=32); the returned owning block selects its chunk slot.
// ============================================================================

@import "./vge_worldspace_bridge.glsl"

/// Computes the local patch and UV together with the inward-biased absolute block that owns it.
void VgeLumonSceneComputeVoxelPatchIdAndUv(
    vec3 worldPosRel,
    vec3 geometricNormal,
    out uint outPatchId,
    out vec2 outPatchUv01,
    out ivec3 outOwningBlock)
{
    // Keep constants in sync with the surface-cache patch layout.
    const int VGE_LUMONSCENE_CHUNK_SIZE = 32; // must be power-of-two for bitmask modulo
    const int VGE_LUMONSCENE_PATCH_SIZE = 4;  // voxels per patch edge
    const int VGE_LUMONSCENE_PATCHES_PER_AXIS = VGE_LUMONSCENE_CHUNK_SIZE / VGE_LUMONSCENE_PATCH_SIZE; // 8

    vec3 n = normalize(geometricNormal);
    vec3 an = abs(n);
    vec3 axisN = vec3(0.0);
    uint axisId = 0u;

    // Axis selection from the geometric normal (chunk terrain is axis-aligned, but keep this robust).
    if (an.x >= an.y && an.x >= an.z)
    {
        if (n.x >= 0.0) { axisN = vec3( 1.0, 0.0, 0.0); axisId = 0u; }
        else            { axisN = vec3(-1.0, 0.0, 0.0); axisId = 1u; }
    }
    else if (an.y >= an.x && an.y >= an.z)
    {
        if (n.y >= 0.0) { axisN = vec3(0.0,  1.0, 0.0); axisId = 2u; }
        else            { axisN = vec3(0.0, -1.0, 0.0); axisId = 3u; }
    }
    else
    {
        if (n.z >= 0.0) { axisN = vec3(0.0, 0.0,  1.0); axisId = 4u; }
        else            { axisN = vec3(0.0, 0.0, -1.0); axisId = 5u; }
    }

    // Bias toward the surface interior so floor() resolves the owning block consistently.
    // Matrix-space -> world: keep a float copy for stable per-voxel fractional coordinates (UV within the face).
    vec3 w = worldPosRel + vge_lumonSceneWorldBlockOffsetRem;

    ivec3 block = VgeMatrixSpacePosToWorldCell(worldPosRel - axisN * 1e-4);
    outOwningBlock = block;

    // Chunk-local cell coords [0..31] (two's-complement & is stable and fast).
    int lx = block.x & (VGE_LUMONSCENE_CHUNK_SIZE - 1);
    int ly = block.y & (VGE_LUMONSCENE_CHUNK_SIZE - 1);
    int lz = block.z & (VGE_LUMONSCENE_CHUNK_SIZE - 1);

    int planeIndex = 0;
    int uCell = 0;
    int vCell = 0;
    float uFrac = 0.0;
    float vFrac = 0.0;

    // Face-local UV basis in world axes:
    // - X faces: U=Z, V=Y
    // - Y faces: U=X, V=Z
    // - Z faces: U=X, V=Y
    if (axisId <= 1u)
    {
        planeIndex = lx;
        uCell = lz; vCell = ly;
        uFrac = fract(w.z); vFrac = fract(w.y);
    }
    else if (axisId <= 3u)
    {
        planeIndex = ly;
        uCell = lx; vCell = lz;
        uFrac = fract(w.x); vFrac = fract(w.z);
    }
    else
    {
        planeIndex = lz;
        uCell = lx; vCell = ly;
        uFrac = fract(w.x); vFrac = fract(w.y);
    }

    int patchU = clamp(uCell >> 2, 0, VGE_LUMONSCENE_PATCHES_PER_AXIS - 1);
    int patchV = clamp(vCell >> 2, 0, VGE_LUMONSCENE_PATCHES_PER_AXIS - 1);

    int inPatchU = uCell - (patchU << 2);
    int inPatchV = vCell - (patchV << 2);

    outPatchUv01 = vec2(
        (float(inPatchU) + clamp(uFrac, 0.0, 1.0)) / float(VGE_LUMONSCENE_PATCH_SIZE),
        (float(inPatchV) + clamp(vFrac, 0.0, 1.0)) / float(VGE_LUMONSCENE_PATCH_SIZE));

    // patchId packs: axis + planeIndex + patchUV tile id.
    uint plane = uint(clamp(planeIndex, 0, VGE_LUMONSCENE_CHUNK_SIZE - 1));
    uint tile = uint((patchV << 3) + patchU);                             // 0..63
    uint patchLinear = (plane << 6) + tile;                              // 0..2047
    outPatchId = 1u + axisId + patchLinear * 6u;                          // 1..12288
}

#endif // VGE_LUMONSCENE_PATCHID_GLSL
