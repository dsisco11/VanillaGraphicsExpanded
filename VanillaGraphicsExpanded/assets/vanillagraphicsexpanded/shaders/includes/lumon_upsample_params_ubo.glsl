// LumOn upsample params UBO
//
// Non-opaque uniforms for lumon_upsample.fsh.

#ifndef LUMON_UPSAMPLE_PARAMS_UBO_GLSL
#define LUMON_UPSAMPLE_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgeLumOnUpsampleParamsUBO
{
    // upsampleDepthSigma.x, upsampleNormalSigma.y, upsampleSpatialSigma.z, holeFillMinConfidence.w
    vec4 upsampleFloats0;

    // holeFillRadius.x, reserved.yzw
    ivec4 upsampleInts0;
} vgeLumOnUpsampleParams;

#define upsampleDepthSigma (vgeLumOnUpsampleParams.upsampleFloats0.x)
#define upsampleNormalSigma (vgeLumOnUpsampleParams.upsampleFloats0.y)
#define upsampleSpatialSigma (vgeLumOnUpsampleParams.upsampleFloats0.z)

#define holeFillMinConfidence (vgeLumOnUpsampleParams.upsampleFloats0.w)
#define holeFillRadius (vgeLumOnUpsampleParams.upsampleInts0.x)

#endif // LUMON_UPSAMPLE_PARAMS_UBO_GLSL
