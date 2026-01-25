#version 330 core

vec2 uv;

out vec4 outColor;

// ============================================================================
// LumOn Debug Visualization Shader
// 
// Renders debug overlays for the LumOn probe grid system.
// This shader runs at the AfterBlit stage to ensure visibility.
//
// Debug Modes:
// 1 = Probe Grid with validity coloring
// 2 = Probe Depth heatmap
// 3 = Probe Normals
// 4 = Scene Depth (linearized)
// 5 = Scene Normals (G-buffer)
// 6 = Temporal Weight (how much history is used)
// 7 = Temporal Rejection Mask (why history was rejected)
// 8 = SH Coefficients (DC + directional magnitude)
// 9 = Interpolation Weights (probe blend visualization)
// 10 = Radiance Overlay (indirect diffuse buffer)
// 11 = Gather Weight (diagnostic; reads indirectHalf alpha)
// 12 = Probe-Atlas Meta Confidence (grayscale)
// 13 = Probe-Atlas Temporal Alpha (confidence-scaled)
// 14 = Probe-Atlas Meta Flags (RGB bit visualization)
// 15 = Probe-Atlas Filtered Radiance (probe-space)
// 16 = Probe-Atlas Filter Delta (abs(filtered - current))
// 17 = Probe-Atlas Gather Input Source (raw vs filtered)
// 45 = Probe-Atlas Current Radiance (post-temporal)
// 46 = Probe-Atlas Gather Input Radiance (actual gather input)
// 47 = Probe-Atlas Hit Distance (log-encoded, from gather input)
// 48 = Probe-Atlas Trace Radiance (pre-temporal)
// 18 = Composite AO (Phase 15)
// 19 = Composite Indirect Diffuse (Phase 15)
// 20 = Composite Indirect Specular (Phase 15)
// 21 = Composite Material (metallic, roughness, ao)
// 22 = Direct Diffuse (Phase 16)
// 23 = Direct Specular (Phase 16)
// 24 = Direct Emissive (Phase 16)
// 25 = Direct Total (diffuse+spec) (Phase 16)
// 31 = World-Probe Irradiance (combined)
// 32 = World-Probe Irradiance (selected level)
// 33 = World-Probe Confidence
// 34 = World-Probe ShortRangeAO Direction
// 35 = World-Probe ShortRangeAO Confidence
// 36 = World-Probe Hit Distance (normalized)
// 37 = World-Probe Meta Flags (heatmap)
// 38 = Blend Weights (screen vs world)
// 39 = Cross-Level Blend (selected L and weights)
// 41 = POM Metrics (heatmap from gBufferNormal.w)
// 42 = Raw Confidences (R=screenConf, G=worldConf, B=sumW)
// 43 = Contribution Only: world-probe (Phase 18)
// 44 = Contribution Only: screen-space (Phase 18)
// ============================================================================

// Import common utilities
@import "./includes/lumon_common.glsl"

// Import SH helpers for mode 8
@import "./includes/lumon_sh.glsl"

// Phase 18: world-probe clipmap sampling + uniforms
@import "./includes/lumon_worldprobe.glsl"

// Import probe-atlas meta helpers for mode 14
@import "./includes/lumon_probe_atlas_meta.glsl"

// Phase 14 velocity helpers
@import "./includes/velocity_common.glsl"

// Phase 15: composite math (shared with lumon_combine)
@import "./includes/lumon_pbr.glsl"

// Import global defines (feature toggles with defaults)
@import "./includes/vge_global_defines.glsl"

// Shared hash helpers
@import "./includes/squirrel3.glsl"

// Shared debug uniforms/helpers + per-program-kind implementations.
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/lumon_debug_common.glsl"
@import "./includes/lumon_debug_probe_anchors.glsl"
@import "./includes/lumon_debug_gbuffer.glsl"
@import "./includes/lumon_debug_temporal.glsl"
@import "./includes/lumon_debug_sh.glsl"
@import "./includes/lumon_debug_indirect.glsl"
@import "./includes/lumon_debug_probe_atlas.glsl"
@import "./includes/lumon_debug_composite.glsl"
@import "./includes/lumon_debug_direct.glsl"
@import "./includes/lumon_debug_velocity.glsl"
@import "./includes/lumon_debug_worldprobe.glsl"

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    
    switch (debugMode) {
        case 1:
            outColor = renderProbeGridDebug(screenPos);
            break;
        case 2:
            outColor = renderProbeDepthDebug(screenPos);
            break;
        case 3:
            outColor = renderProbeNormalDebug(screenPos);
            break;
        case 42:
            outColor = renderWorldProbeRawConfidencesDebug();
            break;
        case 43:
            outColor = renderWorldProbeContributionOnlyDebug();
            break;
        case 44:
            outColor = renderScreenSpaceContributionOnlyDebug();
            break;
        case 4:
            outColor = renderSceneDepthDebug();
            break;
        case 5:
            outColor = renderSceneNormalDebug();
            break;
        case 6:
            outColor = renderTemporalWeightDebug(screenPos);
            break;
        case 7:
            outColor = renderTemporalRejectionDebug(screenPos);
            break;
        case 8:
            outColor = renderSHCoefficientsDebug(screenPos);
            break;
        case 9:
            outColor = renderInterpolationWeightsDebug(screenPos);
            break;
        case 10:
            outColor = renderRadianceOverlayDebug();
            break;
        case 11:
            outColor = renderGatherWeightDebug();
            break;
        case 12:
            outColor = renderProbeAtlasMetaConfidenceDebug();
            break;
        case 13:
            outColor = renderProbeAtlasTemporalAlphaDebug();
            break;
        case 14:
            outColor = renderProbeAtlasMetaFlagsDebug();
            break;
        case 15:
            outColor = renderProbeAtlasFilteredRadianceDebug();
            break;
        case 16:
            outColor = renderProbeAtlasFilterDeltaDebug();
            break;
        case 17:
            outColor = renderProbeAtlasGatherInputSourceDebug();
            break;
        case 45:
            outColor = renderProbeAtlasCurrentRadianceDebug();
            break;
        case 46:
            outColor = renderProbeAtlasGatherInputRadianceDebug();
            break;
        case 47:
            outColor = renderProbeAtlasHitDistanceDebug();
            break;
        case 48:
            outColor = renderProbeAtlasTraceRadianceDebug();
            break;
        case 18:
            outColor = renderCompositeAoDebug();
            break;
        case 19:
            outColor = renderCompositeIndirectDiffuseDebug();
            break;
        case 20:
            outColor = renderCompositeIndirectSpecularDebug();
            break;
        case 21:
            outColor = renderCompositeMaterialDebug();
            break;
        case 22:
            outColor = renderDirectDiffuseDebug();
            break;
        case 23:
            outColor = renderDirectSpecularDebug();
            break;
        case 24:
            outColor = renderDirectEmissiveDebug();
            break;
        case 25:
            outColor = renderDirectTotalDebug();
            break;
        case 26:
            outColor = renderVelocityMagnitudeDebug();
            break;
        case 27:
            outColor = renderVelocityValidityDebug();
            break;
        case 28:
            outColor = renderVelocityPrevUvDebug();
            break;
        case 29:
            outColor = renderMaterialBandsDebug();
            break;
        case 31:
            outColor = renderWorldProbeIrradianceCombinedDebug();
            break;
        case 32:
            outColor = renderWorldProbeIrradianceLevelDebug();
            break;
        case 33:
            outColor = renderWorldProbeConfidenceDebug();
            break;
        case 34:
            outColor = renderWorldProbeAoDirectionDebug();
            break;
        case 35:
            outColor = renderWorldProbeAoConfidenceDebug();
            break;
        case 36:
            outColor = renderWorldProbeDistanceDebug();
            break;
        case 37:
            outColor = renderWorldProbeFlagsHeatmapDebug();
            break;
        case 38:
            outColor = renderWorldProbeBlendWeightsDebug();
            break;
        case 39:
            outColor = renderWorldProbeCrossLevelBlendDebug();
            break;
        case 41:
            outColor = renderPomMetricsDebug();
            break;
        default:
            outColor = vec4(1.0, 0.0, 1.0, 1.0);  // Magenta = unknown mode
            break;
    }
}
