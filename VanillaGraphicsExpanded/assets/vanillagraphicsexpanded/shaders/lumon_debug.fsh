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
// 49 = Probe-Atlas Temporal Rejection (reason bits from atlas temporal)
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

// Phase 22.10: LumonScene surface cache sampling helpers (used by debug modes 52-54).
@import "./includes/lumonscene_surface_cache.glsl"

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
@import "./includes/lumon_debug_tracescene.glsl"
@import "./includes/lumon_debug_scenes_overview.glsl"

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;

    // Legacy dispatcher: select the program-kind implementation based on debugMode.
    switch (debugMode)
    {
        case 1:
        case 2:
        case 3:
            outColor = RenderDebug_ProbeAnchors(screenPos);
            break;

        case 4:
        case 5:
        case 29:
        case 41:
            outColor = RenderDebug_SceneGBuffer(screenPos);
            break;

        case 6:
        case 7:
            outColor = RenderDebug_Temporal(screenPos);
            break;

        case 8:
        case 9:
            outColor = RenderDebug_ShInterpolation(screenPos);
            break;

        case 10:
        case 11:
            outColor = RenderDebug_Indirect(screenPos);
            break;

        case 12:
        case 13:
        case 14:
        case 15:
        case 16:
        case 17:
        case 45:
        case 46:
        case 47:
        case 48:
        case 49:
            outColor = RenderDebug_ProbeAtlas(screenPos);
            break;

        case 18:
        case 19:
        case 20:
        case 21:
            outColor = RenderDebug_Composite(screenPos);
            break;

        case 22:
        case 23:
        case 24:
        case 25:
            outColor = RenderDebug_Direct(screenPos);
            break;

        case 26:
        case 27:
        case 28:
            outColor = RenderDebug_Velocity(screenPos);
            break;

        case 31:
        case 32:
        case 33:
        case 34:
        case 35:
        case 36:
        case 37:
        case 38:
        case 39:
        case 42:
        case 43:
        case 44:
            outColor = RenderDebug_WorldProbe(screenPos);
            break;

        case 55:
        case 56:
        case 57:
            outColor = RenderDebug_TraceScene(screenPos);
            break;

        case 58:
            outColor = RenderDebug_LumOnScenesOverview(screenPos);
            break;

        default:
            outColor = vec4(1.0, 0.0, 1.0, 1.0);
            break;
    }
}
