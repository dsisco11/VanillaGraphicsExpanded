#ifndef VGE_LUMONSCENE_MATERIAL_PACKING_GLSL
#define VGE_LUMONSCENE_MATERIAL_PACKING_GLSL
// ============================================================================
// LumonScene material packing helpers (Phase 22/23)
//
// Goals:
// - Keep MaterialAtlas small and stable:
//   - RGBA8: RG = oct-encoded normal, BA = 16-bit surfaceId
// - Keep TraceScene material palette compact:
//   - RGBA32UI: packs 6x 16-bit surfaceIds (block faces 0..5)
//
// Notes:
// - Block face indices follow VintageStory conventions:
//   0=North, 1=East, 2=South, 3=West, 4=Up, 5=Down
// - Voxel patch axisId uses:
//   0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
// ============================================================================

@import "./lumon_octahedral.glsl"

uint VgeLumonSceneAxisIdToBlockFaceIndex(uint axisId)
{
    // Assumes +X=East, -X=West, +Z=South, -Z=North (VintageStory usual axis convention).
    if (axisId == 0u) return 1u; // +X -> East
    if (axisId == 1u) return 3u; // -X -> West
    if (axisId == 2u) return 4u; // +Y -> Up
    if (axisId == 3u) return 5u; // -Y -> Down
    if (axisId == 4u) return 2u; // +Z -> South
    if (axisId == 5u) return 0u; // -Z -> North
    return 4u;
}

uint VgeLumonSceneUnpackFaceSurfaceId(uvec4 packedFaces, uint faceIndex)
{
    // packedFaces:
    // x: face0 | face1<<16
    // y: face2 | face3<<16
    // z: face4 | face5<<16
    uint v = 0u;
    if (faceIndex <= 1u) v = packedFaces.x;
    else if (faceIndex <= 3u) v = packedFaces.y;
    else v = packedFaces.z;

    if ((faceIndex & 1u) == 0u) return v & 0xFFFFu;
    return (v >> 16u) & 0xFFFFu;
}

vec2 VgeLumonSceneEncodeNormalOct01(vec3 normalWS)
{
    vec2 uv = lumonDirectionToOctahedralUV(normalize(normalWS));
    return clamp(uv, vec2(0.0), vec2(1.0));
}

vec3 VgeLumonSceneDecodeNormalOct01(vec2 oct01)
{
    vec2 uv = clamp(oct01, vec2(0.0), vec2(1.0));
    return normalize(lumonOctahedralUVToDirection(uv));
}

ivec2 VgeLumonSceneSurfaceLutUv(uint surfaceId16)
{
    // SurfaceLut is a 256x256 usampler2D addressed by 16-bit surfaceId.
    // x = low 8 bits, y = high 8 bits.
    return ivec2(int(surfaceId16 & 255u), int(surfaceId16 >> 8u));
}

vec4 VgeLumonScenePackMaterialAtlas(vec3 normalWS, uint surfaceId16)
{
    vec2 oct01 = VgeLumonSceneEncodeNormalOct01(normalWS);
    float lo = float(surfaceId16 & 255u) / 255.0;
    float hi = float((surfaceId16 >> 8u) & 255u) / 255.0;
    return vec4(oct01, lo, hi);
}

uint VgeLumonSceneUnpackSurfaceIdFromMaterialAtlas(vec4 mat)
{
    uint lo = uint(clamp(floor(mat.b * 255.0 + 0.5), 0.0, 255.0));
    uint hi = uint(clamp(floor(mat.a * 255.0 + 0.5), 0.0, 255.0));
    return lo | (hi << 8u);
}

#endif // VGE_LUMONSCENE_MATERIAL_PACKING_GLSL
