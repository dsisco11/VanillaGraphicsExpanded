// LumOn probe-atlas / probe gather params UBO
//
// Non-opaque parameters shared across multiple LumOn probe shaders.

#ifndef LUMON_PROBE_PARAMS_UBO_GLSL
#define LUMON_PROBE_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgeLumOnProbeParamsUBO
{
    // indirectTint.xyz, intensity.w
    vec4 indirectTint_intensity;

    // temporalAlpha.x, hitDistanceRejectThreshold.y, hitDistanceSigma.z, leakThreshold.w
    vec4 probeFloats0;

    // filterRadius.x, sampleStride.y, reserved.zw
    ivec4 probeInts0;

    // depthDiscontinuityThreshold.x, suppressWorldProbeRadiance.y, reserved.zw
    vec4 anchorFloats0;
} vgeLumOnProbeParams;

// Legacy uniform names (macro aliases)
#define indirectTint (vgeLumOnProbeParams.indirectTint_intensity.xyz)
#define intensity (vgeLumOnProbeParams.indirectTint_intensity.w)

#define temporalAlpha (vgeLumOnProbeParams.probeFloats0.x)
#define hitDistanceRejectThreshold (vgeLumOnProbeParams.probeFloats0.y)
#define hitDistanceSigma (vgeLumOnProbeParams.probeFloats0.z)
#define leakThreshold (vgeLumOnProbeParams.probeFloats0.w)

#define filterRadius (vgeLumOnProbeParams.probeInts0.x)
#define sampleStride (vgeLumOnProbeParams.probeInts0.y)

#define suppressWorldProbeRadiance (vgeLumOnProbeParams.anchorFloats0.y > 0.5)

#define depthDiscontinuityThreshold (vgeLumOnProbeParams.anchorFloats0.x)

#endif // LUMON_PROBE_PARAMS_UBO_GLSL
