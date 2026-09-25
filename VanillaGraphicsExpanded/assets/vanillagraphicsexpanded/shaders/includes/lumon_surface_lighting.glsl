#ifndef LUMON_SURFACE_LIGHTING_GLSL
#define LUMON_SURFACE_LIGHTING_GLSL
@import "./vge_ubo_bindings.glsl"
/** Shared surface lighting parameters; all positions retain integer chunk anchors. */
layout(std140, binding = 16) uniform SurfaceLightingParams
{
    uvec4 layoutInfo; // tile edge, tiles per axis, tiles per layer, operation (0 seed, 1 trace, 2 combine, 3 reset)
    uvec4 sampling;   // batch texels, rays, steps, frame
    ivec4 slotOrigin;
    ivec4 slotDimensions;
    ivec4 slotRing;
    uvec4 policy;     // explicit material emission, authoritative world height (0 unknown), maximum accumulated history, reserved
} lighting;
/** Captured patchIdentity identity and local basis; matches the capture producer. */
struct SurfacePatch
{
    vec4 origin; vec4 axisU; vec4 axisV; vec4 normal;
    uint baseX; uint baseY; uint sizeX; uint sizeY;
    uint chunkSlot; uint patchId; uint generation; uint integerOrigin;
};
layout(std430, binding = 1) readonly buffer SurfacePatches { SurfacePatch patches[]; };
layout(std430, binding = 2) readonly buffer SurfaceSlots { ivec4 slots[]; };
layout(std430, binding = 3) readonly buffer SurfaceReady { uint ready[]; };
layout(binding = 17) uniform sampler2DArray previousOutgoing;
layout(binding = 18) uniform usampler2DArray surfacePages;
layout(binding = 16) uniform sampler2DArray capturedMaterial;

/** Decodes a physical page with the same shared atlas layout as allocation. */
ivec3 surfaceAddress(uint id, ivec2 texel)
{
    uint index = id - 1u, local = index % lighting.layoutInfo.z;
    return ivec3(ivec2(local % lighting.layoutInfo.y, local / lighting.layoutInfo.y) * int(lighting.layoutInfo.x) + texel,
        int(index / lighting.layoutInfo.z));
}
/** Computes integer floor division for signed world coordinates. */
ivec3 surfaceChunk(ivec3 cell) { return cell >> 5; }
/** Resolves a geometric hit into an initialized texel of the previous lighting generation. */
bool sampleSurfaceLighting(ivec3 cell, ivec3 normal, vec3 fraction, uint materialId, out vec3 radiance)
{
    radiance = vec3(0);
    if (any(lessThanEqual(lighting.slotDimensions.xyz, ivec3(0))) || materialId == 0u || dot(vec3(abs(normal)), vec3(1)) != 1.0) return false;
    ivec3 chunk = surfaceChunk(cell);
    ivec3 localChunk = chunk - lighting.slotOrigin.xyz;
    ivec3 dims = lighting.slotDimensions.xyz;
    if (any(lessThan(localChunk, ivec3(0))) || any(greaterThanEqual(localChunk, dims))) return false;
    ivec3 ring = (localChunk + lighting.slotRing.xyz) % dims;
    uint slot = uint((ring.y * dims.z + ring.z) * dims.x + ring.x);
    if (slot >= uint(slots.length()) || any(notEqual(slots[slot].xyz, chunk * 32))) return false;
    ivec3 p = cell - chunk * 32;
    uint axis = normal.x != 0 ? (normal.x > 0 ? 0u : 1u) : normal.y != 0 ? (normal.y > 0 ? 2u : 3u) : (normal.z > 0 ? 4u : 5u);
    int plane = axis < 2u ? p.x : axis < 4u ? p.y : p.z;
    ivec2 uvCell = axis < 2u ? p.zy : axis < 4u ? p.xz : p.xy;
    vec2 uvFraction = axis < 2u ? fraction.zy : axis < 4u ? fraction.xz : fraction.xy;
    uint patchIdentity = 1u + 6u * uint(plane * 64 + (uvCell.y / 4) * 8 + uvCell.x / 4) + axis;
    uint entry = texelFetch(surfacePages, ivec3(int(patchIdentity % 128u), int(patchIdentity / 128u), int(slot)), 0).r;
    uint id = entry & 0xffffffu, flags = entry >> 24u;
    if (id == 0u || id >= uint(ready.length()) || ready[id] == 0u || (flags & 11u) != 1u) return false;
    if (id >= uint(patches.length())) return false;
    SurfacePatch meta = patches[id];
    if (meta.chunkSlot != slot || meta.patchId != patchIdentity || meta.generation != (uint(slots[slot].w) & 65535u)) return false;
    vec2 uv = (vec2(uvCell % 4) + clamp(uvFraction, vec2(0), vec2(0.99999))) * 0.25;
    ivec3 address = surfaceAddress(id, min(ivec2(uv * float(lighting.layoutInfo.x)), ivec2(lighting.layoutInfo.x) - 1));
    vec4 captured = texelFetch(capturedMaterial, address, 0);
    uint capturedId = uint(round(captured.b * 255.0)) | (uint(round(captured.a * 255.0)) << 8u);
    if (capturedId != materialId) return false;
    vec4 value = texelFetch(previousOutgoing, address, 0);
    if (value.a != 1.0 || any(isnan(value)) || any(isinf(value))) return false;
    radiance = max(value.rgb, vec3(0));
    return true;
}
#endif
