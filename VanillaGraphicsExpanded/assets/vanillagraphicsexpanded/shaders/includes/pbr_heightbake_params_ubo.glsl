// PBR height-bake params UBO
//
// Non-opaque uniforms shared across pbr_heightbake_*.fsh.

#ifndef PBR_HEIGHTBAKE_PARAMS_UBO_GLSL
#define PBR_HEIGHTBAKE_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

// Packed gaussian weights: 17*vec4 = 68 weights (we use 65).
#define VGE_HEIGHTBAKE_GAUSS_WEIGHTS_VEC4 17

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgePbrHeightBakeParamsUBO
{
    // u_fineSize.xy
    ivec4 uFineSize;

    // u_coarseSize.xy
    ivec4 uCoarseSize;

    // u_size.xy
    ivec4 uSize;

    // u_dir.xy
    ivec4 uDir;

    // x=u_radius, y=u_relContrast, z/w reserved
    ivec4 uInts0;

    // u_eps.x, u_vMax.y, reserved.zw
    vec4 uFloats0;

    // u_w.xyz
    vec4 uCombineW;

    // u_gain.x, u_maxSlope.y, u_edgeT.xy (z,w)
    vec4 uGradient0;

    // u_atlasRectPx.xyzw
    ivec4 uAtlasRectPx;

    // u_outSize.xy
    ivec4 uOutSize;

    // u_solverSize.xy
    ivec4 uSolverSize;

    // u_tileSize.xy
    ivec4 uTileSize;

    // u_viewportOrigin.xy
    ivec4 uViewportOrigin;

    // u_mean.x, u_invNeg.y, u_invPos.z, u_heightStrength.w
    vec4 uNormalize0;

    // u_gamma.x, reserved.yzw
    vec4 uNormalize1;

    // u_normalStrength.x, u_normalScale.y, u_depthScale.z, u_alphaCutoff.w
    vec4 uPack0;

    vec4 uWeightsPacked[VGE_HEIGHTBAKE_GAUSS_WEIGHTS_VEC4];
} vgeHeightBake;

// Legacy uniform name aliases
#define u_fineSize (vgeHeightBake.uFineSize.xy)
#define u_coarseSize (vgeHeightBake.uCoarseSize.xy)

#define u_size (vgeHeightBake.uSize.xy)
#define u_dir (vgeHeightBake.uDir.xy)

#define u_radius (vgeHeightBake.uInts0.x)
#define u_relContrast (vgeHeightBake.uInts0.y)

#define u_eps (vgeHeightBake.uFloats0.x)
#define u_vMax (vgeHeightBake.uFloats0.y)

#define u_w (vgeHeightBake.uCombineW.xyz)

#define u_gain (vgeHeightBake.uGradient0.x)
#define u_maxSlope (vgeHeightBake.uGradient0.y)
#define u_edgeT (vgeHeightBake.uGradient0.zw)

#define u_atlasRectPx (vgeHeightBake.uAtlasRectPx)
#define u_outSize (vgeHeightBake.uOutSize.xy)

#define u_solverSize (vgeHeightBake.uSolverSize.xy)
#define u_tileSize (vgeHeightBake.uTileSize.xy)
#define u_viewportOrigin (vgeHeightBake.uViewportOrigin.xy)

#define u_mean (vgeHeightBake.uNormalize0.x)
#define u_invNeg (vgeHeightBake.uNormalize0.y)
#define u_invPos (vgeHeightBake.uNormalize0.z)
#define u_heightStrength (vgeHeightBake.uNormalize0.w)
#define u_gamma (vgeHeightBake.uNormalize1.x)

#define u_normalStrength (vgeHeightBake.uPack0.x)
#define u_normalScale (vgeHeightBake.uPack0.y)
#define u_depthScale (vgeHeightBake.uPack0.z)
#define u_alphaCutoff (vgeHeightBake.uPack0.w)

float VgeHeightBakeGaussWeight(int i)
{
    i = clamp(i, 0, 64);
    int vec4Index = i >> 2;
    int lane = i & 3;
    vec4 v = vgeHeightBake.uWeightsPacked[vec4Index];
    return (lane == 0) ? v.x : (lane == 1) ? v.y : (lane == 2) ? v.z : v.w;
}

#endif // PBR_HEIGHTBAKE_PARAMS_UBO_GLSL
