#ifndef LUMON_REPROJECTION_GLSL
#define LUMON_REPROJECTION_GLSL

// ============================================================================
// LumOn Reprojection Utilities
//
// Conventions:
// - UV space is [0,1] with origin at bottom-left (OpenGL-style).
// - NDC to UV: uv = ndc * 0.5 + 0.5.
// - Velocity convention: velocityUv = currUv - prevUv.
//   Therefore: prevUv = currUv - velocityUv.
// ============================================================================

bool lumonIsNanFloat(float v)
{
    // NaN is the only float where v != v.
    return !(v == v);
}

bool lumonIsNanVec2(vec2 v)
{
    return lumonIsNanFloat(v.x) || lumonIsNanFloat(v.y);
}

vec2 lumonNdcToUv(vec2 ndc)
{
    return ndc * 0.5 + 0.5;
}

/**
 * Computes previous-frame UV for the current pixel, using depth reconstruction.
 *
 * Inputs:
 * - currUv: current screen UV [0,1]
 * - depthRaw: hardware depth [0,1] from the current frame (non-linear)
 * - invCurrViewProj: inverse of current ViewProj matrix
 * - prevViewProj: previous frame ViewProj matrix
 *
 * Returns true when prevUv is valid/in-bounds.
 */
bool lumonComputePrevUvFromDepth(
    vec2 currUv,
    float depthRaw,
    mat4 invCurrViewProj,
    mat4 prevViewProj,
    out vec2 prevUv)
{
    prevUv = vec2(0.0);

    // Reject empty/invalid depth.
    // Note: Depth=1.0 usually means far plane / sky.
    if (depthRaw <= 0.0 || depthRaw >= 1.0)
    {
        return false;
    }

    // Reconstruct world position from current depth.
    vec4 currClip = vec4(currUv * 2.0 - 1.0, depthRaw * 2.0 - 1.0, 1.0);
    vec4 worldPosH = invCurrViewProj * currClip;
    if (abs(worldPosH.w) < 1e-8)
    {
        return false;
    }

    vec3 worldPos = worldPosH.xyz / worldPosH.w;

    // Project into previous clip space.
    vec4 prevClip = prevViewProj * vec4(worldPos, 1.0);
    if (prevClip.w <= 1e-8)
    {
        return false;
    }

    vec2 prevNdc = prevClip.xy / prevClip.w;
    prevUv = lumonNdcToUv(prevNdc);

    if (lumonIsNanVec2(prevUv))
    {
        return false;
    }

    // Bounds check.
    if (prevUv.x < 0.0 || prevUv.x > 1.0 || prevUv.y < 0.0 || prevUv.y > 1.0)
    {
        return false;
    }

    return true;
}

/**
 * Computes per-pixel velocity in UV units, using depth reconstruction.
 *
 * velocityUv = currUv - prevUv
 */
bool lumonComputeVelocityUvFromDepth(
    vec2 currUv,
    float depthRaw,
    mat4 invCurrViewProj,
    mat4 prevViewProj,
    out vec2 velocityUv,
    out vec2 prevUv)
{
    bool valid = lumonComputePrevUvFromDepth(currUv, depthRaw, invCurrViewProj, prevViewProj, prevUv);
    velocityUv = valid ? (currUv - prevUv) : vec2(0.0);
    return valid;
}

#endif // LUMON_REPROJECTION_GLSL
