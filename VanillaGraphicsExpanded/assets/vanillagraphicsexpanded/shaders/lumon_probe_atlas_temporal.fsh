#version 330 core

layout(location = 0) out vec4 outRadiance;  // RGB = blended radiance, A = blended hit distance (log-encoded)
layout(location = 1) out vec2 outMeta;      // R = confidence, G = uintBitsToFloat(flags)

// ============================================================================
// LumOn Octahedral Temporal Pass
// 
// Per-texel temporal blending for octahedral radiance cache.
// Only blends texels that were traced this frame; preserves others unchanged.
// Uses hit-distance delta for per-texel disocclusion detection.
//
// See: LumOn.05-Temporal.md Section 7.2
// ============================================================================

// Import common utilities
@import "lumon_common.fsh"

// Import octahedral mapping utilities
@import "lumon_octahedral.glsl"

// Import probe-atlas meta helpers
@import "lumon_probe_atlas_meta.glsl"

// ============================================================================
// Uniforms
// ============================================================================

// Current frame trace output (from octahedral trace pass)
// Contains fresh data for traced texels, history copies for non-traced texels
uniform sampler2D octahedralCurrent;

// Current frame trace meta output (from probe-atlas trace pass)
uniform sampler2D probeAtlasMetaCurrent;

// History from previous frame (after last swap)
uniform sampler2D octahedralHistory;

// Probe anchors for validity check
uniform sampler2D probeAnchorPosition;  // xyz = posWS, w = validity

// Probe grid parameters
uniform vec2 probeGridSize;

// Temporal distribution parameters
uniform int frameIndex;
uniform int texelsPerFrame;  // How many texels traced per probe per frame (default 8)

// Temporal blending parameters
uniform float temporalAlpha;              // Base blend factor (e.g., 0.9)
uniform float hitDistanceRejectThreshold; // Relative threshold (e.g., 0.3 = 30%)

// ============================================================================
// Temporal Distribution Logic
// ============================================================================

/**
 * Determine if this octahedral texel was traced this frame.
 * Must match the logic in lumon_probe_trace_octahedral.fsh.
 */
bool wasTracedThisFrame(ivec2 octTexel, int probeIndex) {
    // Linear texel index within the probe's 8×8 tile
    int texelIndex = octTexel.y * LUMON_OCTAHEDRAL_SIZE + octTexel.x;
    
    // Number of batches (with 64 texels and 8 texels/frame = 8 batches)
    int numBatches = (LUMON_OCTAHEDRAL_SIZE * LUMON_OCTAHEDRAL_SIZE) / texelsPerFrame;
    int batch = texelIndex / texelsPerFrame;
    
    // Per-probe jitter to avoid all probes tracing same texels
    int jitteredFrame = (frameIndex + probeIndex) % numBatches;
    
    return batch == jitteredFrame;
}

// ============================================================================
// Neighborhood Clamping (within probe tile)
// ============================================================================

/**
 * Get min/max of 3×3 neighborhood within the probe's octahedral tile.
 * This prevents ghosting artifacts from accumulated history drift.
 */
void getNeighborhoodMinMax(ivec2 probeCoord, ivec2 octTexel,
                           out vec4 minVal, out vec4 maxVal) {
    minVal = vec4(1e10);
    maxVal = vec4(-1e10);
    
    ivec2 atlasOffset = probeCoord * LUMON_OCTAHEDRAL_SIZE;
    
    for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
            // Clamp neighbor to valid tile bounds [0, 7]
            ivec2 neighborTexel = clamp(octTexel + ivec2(dx, dy), 
                                        ivec2(0), ivec2(LUMON_OCTAHEDRAL_SIZE - 1));
            ivec2 neighborAtlas = atlasOffset + neighborTexel;
            
            vec4 sample = texelFetch(octahedralCurrent, neighborAtlas, 0);
            minVal = min(minVal, sample);
            maxVal = max(maxVal, sample);
        }
    }
}

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    // Calculate probe and texel coordinates from atlas position
    ivec2 atlasCoord = ivec2(gl_FragCoord.xy);
    ivec2 probeCoord = atlasCoord / LUMON_OCTAHEDRAL_SIZE;
    ivec2 octTexel = atlasCoord % LUMON_OCTAHEDRAL_SIZE;
    
    // Clamp probe coordinates to valid range
    ivec2 probeGridSizeI = ivec2(probeGridSize);
    probeCoord = clamp(probeCoord, ivec2(0), probeGridSizeI - 1);
    
    // Linear probe index for jitter calculation
    int probeIndex = probeCoord.y * probeGridSizeI.x + probeCoord.x;
    
    // Check probe validity
    float probeValid = texelFetch(probeAnchorPosition, probeCoord, 0).w;
    
    // Invalid probe: output zero (no contribution)
    if (probeValid < 0.5) {
        outRadiance = vec4(0.0);
        outMeta = lumonEncodeMeta(0.0, 0u);
        return;
    }
    
    // Load current frame data (from trace pass)
    vec4 current = texelFetch(octahedralCurrent, atlasCoord, 0);

    // Meta is currently pass-through from trace output (Phase 10 will make it temporal-aware)
    vec2 metaCurrent = texelFetch(probeAtlasMetaCurrent, atlasCoord, 0).xy;
    
    // Load history data
    vec4 history = texelFetch(octahedralHistory, atlasCoord, 0);
    
    // Check if this texel was traced this frame
    if (!wasTracedThisFrame(octTexel, probeIndex)) {
        // Not traced this frame: preserve the trace output unchanged
        // (trace shader already copied history for non-traced texels)
        outRadiance = current;
        outMeta = metaCurrent;
        return;
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // Traced texel: perform temporal blending with validation
    // ═══════════════════════════════════════════════════════════════════════
    
    // Decode hit distances for validation
    float currentHitDist = lumonDecodeHitDistance(current.a);
    float historyHitDist = lumonDecodeHitDistance(history.a);
    
    // Validate history using hit-distance comparison
    bool historyValid = historyHitDist > 0.001;  // Has valid history data?
    
    if (historyValid) {
        // Check if hit distance changed significantly (disocclusion)
        float maxDist = max(currentHitDist, historyHitDist);
        if (maxDist > 0.001) {
            float relativeDiff = abs(currentHitDist - historyHitDist) / maxDist;
            historyValid = relativeDiff < hitDistanceRejectThreshold;
        }
    }
    
    vec4 result;
    
    if (historyValid) {
        // Get neighborhood bounds for clamping (prevents ghosting)
        vec4 minVal, maxVal;
        getNeighborhoodMinMax(probeCoord, octTexel, minVal, maxVal);
        
        // Clamp history to current neighborhood
        vec4 clampedHistory = clamp(history, minVal, maxVal);
        
        // Blend current with clamped history
        // Note: We blend both radiance (RGB) and hit distance (A)
        result = mix(current, clampedHistory, temporalAlpha);
    } else {
        // Disoccluded: use current frame only (reset)
        result = current;
    }
    
    outRadiance = result;
    outMeta = metaCurrent;
}
