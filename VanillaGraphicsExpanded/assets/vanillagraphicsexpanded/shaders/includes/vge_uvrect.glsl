#ifndef VGE_UVRECT_GLSL
#define VGE_UVRECT_GLSL

// Vertex-safe helpers for computing a stable atlas tile rect for a face.
//
// Intended use:
// - Vertex shader (SSBO path): derive per-face `uvBase` and `uvExtent` from FaceData packing.
// - Fragment shader (later): clamp POM/offset UVs to [uvBase, uvBase + uvExtent].
//
// Notes:
// - This matches the packing conventions used by Vintage Story's `FaceData` + `UnpackUv(...)`
//   in `shaderincludes/vertexflagbits.ash`.
// - `packedUv` is the base UV (u16,u16) in 1/32768 units.
// - `packedUvSize` stores signed width/height (15-bit magnitude + sign bit) and rotation flags.

const float VGE_UV_PACK_SCALE = 1.0 / 32768.0;

void VgeDecodeFaceUvExtent(int packedUvSize, out float du, out float dv)
{
    // Mirrors the signed decode in UnpackUv(...).
    // Low 16 bits: U extent (15-bit magnitude + sign bit at 0x4000; rotate at 0x8000)
    // High 16 bits: V extent (15-bit magnitude + sign bit at 0x4000_0000)

    // Keep the small negative epsilon from the engine code to avoid sampling exactly on the edge.
    const float eps = 1e-8;

    int uvs = packedUvSize;

    int uMag = (uvs & 0x7FFF);
    int uSignBias = ((uvs & 0x4000) << 1);      // 0x8000 when sign bit is set
    du = (float(uMag - uSignBias) - eps) * VGE_UV_PACK_SCALE;

    int vMag = ((uvs >> 16) & 0x7FFF);
    int vSignBias = ((uvs & 0x40000000) >> 15); // 0x8000 when sign bit is set
    dv = (float(vMag - vSignBias) - eps) * VGE_UV_PACK_SCALE;
}

void VgeComputeFaceUvRect(int packedUv, int packedUvSize, float subpixelPaddingX, float subpixelPaddingY, out vec2 outUvBase, out vec2 outUvExtent)
{
    // Origin is one corner of the quad.
    vec2 uv0 = vec2(float(packedUv & 0xFFFF), float((packedUv >> 16) & 0xFFFF)) * VGE_UV_PACK_SCALE;

    float du;
    float dv;
    VgeDecodeFaceUvExtent(packedUvSize, du, dv);

    // Convert signed extents into (min corner, extent).
    vec2 uvMin = uv0 + vec2(min(0.0, du), min(0.0, dv));
    vec2 uvMax = uv0 + vec2(max(0.0, du), max(0.0, dv));

    // Conservative shrink to reduce bleed on atlases. This is symmetric and intentionally simple;
    // the engine's per-corner padding is asymmetric depending on winding/sign.
    vec2 pad = vec2(subpixelPaddingX, subpixelPaddingY);
    uvMin += pad;
    uvMax -= pad;

    outUvBase = uvMin;
    outUvExtent = max(uvMax - uvMin, vec2(0.0));
}

#endif // VGE_UVRECT_GLSL
