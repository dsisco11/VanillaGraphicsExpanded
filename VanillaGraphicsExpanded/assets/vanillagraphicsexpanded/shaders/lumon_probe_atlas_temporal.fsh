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
@import "./includes/lumon_common.glsl"

// Import global defines (loop-bound knobs)
@import "./includes/vge_global_defines.glsl"

// Import octahedral mapping utilities
@import "./includes/lumon_octahedral.glsl"

// Import probe-atlas meta helpers
@import "./includes/lumon_probe_atlas_meta.glsl"

// Phase 14 velocity helpers
@import "./includes/velocity_common.glsl"

// Phase 23: shared per-frame state via UBOs.
#define LUMON_UBO_ENABLE_ALIASES
@import "./includes/lumon_ubos.glsl"

// ============================================================================
// Uniforms
// ============================================================================

// Current frame trace output (from octahedral trace pass)
// Contains fresh data for traced texels, history copies for non-traced texels
uniform sampler2D octahedralCurrent;

// Current frame trace meta output (from probe-atlas trace pass)
uniform sampler2D probeAtlasMetaCurrent;

// Meta history from previous frame (after last swap)
uniform sampler2D probeAtlasMetaHistory;

// History from previous frame (after last swap)
uniform sampler2D octahedralHistory;

// Probe anchors for validity check
uniform sampler2D probeAnchorPosition;  // xyz = posWS, w = validity

// Phase 14 velocity buffer (RGBA32F): RG = currUv - prevUv, A = packed flags
uniform sampler2D velocityTex;

// PMJ jitter sequence texture (still a sampler; cycle length is supplied via UBO).
uniform sampler2D pmjJitter;

// Temporal blending parameters
uniform float temporalAlpha;              // Base blend factor (e.g., 0.9)
uniform float hitDistanceRejectThreshold; // Relative threshold (e.g., 0.3 = 30%)

// Phase 14: velocity reprojection toggles
// (Now supplied by the per-frame UBO.)

// ============================================================================
// Velocity-Based Reprojection Helpers
// ============================================================================

vec2 computeProbeScreenUv(ivec2 probeCoord, ivec2 probeGridSizeI)
{
    vec2 baseUV = lumonProbeToScreenUV(probeCoord, float(probeSpacing), screenSize);

    // Apply the same deterministic jitter sequence used by the probe-anchor pass.
    if (anchorJitterEnabled != 0 && anchorJitterScale > 0.0 && pmjCycleLength > 0)
    {
        int probeIndex = probeCoord.x + probeCoord.y * probeGridSizeI.x;
        int idx = (frameIndex + probeIndex) % pmjCycleLength;
        vec2 u = texelFetch(pmjJitter, ivec2(idx, 0), 0).rg;
        vec2 jitter = u - vec2(0.5);

        float maxOffsetPx = float(probeSpacing) * anchorJitterScale;
        vec2 jitterUV = (jitter * maxOffsetPx) / screenSize;

        vec2 uvPad = vec2(0.5) / screenSize;
        baseUV = clamp(baseUV + jitterUV, uvPad, vec2(1.0) - uvPad);
    }

    return baseUV;
}

void computeHistoryAtlasCoord(
    ivec2 atlasCoord,
    ivec2 probeCoord,
    ivec2 octTexel,
    ivec2 probeGridSizeI,
    out ivec2 historyAtlasCoord,
    out bool usedVelocityReprojection,
    out bool rejectHistoryFromVelocity,
    out uint temporalRejectBits)
{
    historyAtlasCoord = atlasCoord;
    usedVelocityReprojection = false;
    rejectHistoryFromVelocity = false;
    temporalRejectBits = 0u;

    if (enableVelocityReprojection == 0)
    {
        return;
    }

    // Sample velocity at the probe's screen-space anchor point.
    vec2 currUv = computeProbeScreenUv(probeCoord, probeGridSizeI);
    vec4 velSample = texture(velocityTex, currUv);
    uint velFlags = lumonVelocityDecodeFlags(velSample);
    vec2 velUv = lumonVelocityDecodeUv(velSample);

    if (!lumonVelocityIsValid(velFlags) || lumonIsNanVec2(velUv))
    {
        temporalRejectBits |= LUMON_META_TEMPREJ_VELOCITY_INVALID;
        return;
    }

    float velMag = lumonVelocityMagnitude(velUv);
    if (velMag > velocityRejectThreshold)
    {
        temporalRejectBits |= LUMON_META_TEMPREJ_VELOCITY_TOO_LARGE;
        rejectHistoryFromVelocity = true;
        return;
    }

    vec2 prevUv = currUv - velUv;
    if (prevUv.x < 0.0 || prevUv.x > 1.0 || prevUv.y < 0.0 || prevUv.y > 1.0)
    {
        temporalRejectBits |= LUMON_META_TEMPREJ_REPROJ_OOB;
        rejectHistoryFromVelocity = true;
        return;
    }

    // Map the reprojected UV back into a probe cell.
    ivec2 prevPx = ivec2(prevUv * screenSize);
    ivec2 prevProbeCoord = prevPx / max(probeSpacing, 1);
    prevProbeCoord = clamp(prevProbeCoord, ivec2(0), probeGridSizeI - 1);

    historyAtlasCoord = prevProbeCoord * LUMON_OCTAHEDRAL_SIZE + octTexel;
    usedVelocityReprojection = true;
}

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
    int numBatches = (LUMON_OCTAHEDRAL_SIZE * LUMON_OCTAHEDRAL_SIZE) / VGE_LUMON_ATLAS_TEXELS_PER_FRAME;
    int batch = texelIndex / VGE_LUMON_ATLAS_TEXELS_PER_FRAME;
    
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

    // Load current + history meta
    vec2 metaCurrent = texelFetch(probeAtlasMetaCurrent, atlasCoord, 0).xy;
    vec2 metaHistory = texelFetch(probeAtlasMetaHistory, atlasCoord, 0).xy;

    float confCurrent; uint flagsCurrent;
    float confHistory; uint flagsHistory;
    lumonDecodeMeta(metaCurrent, confCurrent, flagsCurrent);
    lumonDecodeMeta(metaHistory, confHistory, flagsHistory);
    
    // Determine history reprojection for this probe (Phase 14)
    ivec2 historyAtlasCoord;
    bool usedVelocityReprojection;
    bool rejectHistoryFromVelocity;
    uint temporalRejectBits;
    computeHistoryAtlasCoord(
        atlasCoord,
        probeCoord,
        octTexel,
        probeGridSizeI,
        historyAtlasCoord,
        usedVelocityReprojection,
        rejectHistoryFromVelocity,
        temporalRejectBits);

    // Load history data (optionally reprojected)
    vec4 history = texelFetch(octahedralHistory, historyAtlasCoord, 0);

    // Load history meta at the same history coordinate (for confidence/blending)
    if (usedVelocityReprojection)
    {
        vec2 metaHistoryReproj = texelFetch(probeAtlasMetaHistory, historyAtlasCoord, 0).xy;
        lumonDecodeMeta(metaHistoryReproj, confHistory, flagsHistory);
    }
    
    // Check if this texel was traced this frame
    if (!wasTracedThisFrame(octTexel, probeIndex)) {
        // Not traced this frame:
        // - If velocity reprojection is enabled and usable, shift history into this probe cell.
        // - Otherwise, preserve the trace output unchanged (trace shader already copied history).
        if (enableVelocityReprojection != 0 && usedVelocityReprojection) {
            outRadiance = history;
            outMeta = lumonEncodeMeta(confHistory, flagsHistory | temporalRejectBits);
        } else {
            outRadiance = current;
            outMeta = lumonEncodeMeta(confCurrent, flagsCurrent | temporalRejectBits);
        }
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

    if (!historyValid)
    {
        temporalRejectBits |= LUMON_META_TEMPREJ_HISTORY_INVALID;
    }

    // Phase 14: conservative rejection on large motion/out-of-bounds reprojection.
    if (historyValid && rejectHistoryFromVelocity)
    {
        historyValid = false;
    }

    // Reject history if history confidence is very low (invalid / unreliable)
    // This is the simplest confidence-aware reset policy.
    if (historyValid) {
        if (confHistory <= 0.05)
        {
            temporalRejectBits |= LUMON_META_TEMPREJ_CONFIDENCE_LOW;
            historyValid = false;
        }
    }

    // Reject history when hit/miss classification changes (more robust than hit distance alone)
    if (historyValid) {
        bool curHit = (flagsCurrent & LUMON_META_HIT) != 0u;
        bool histHit = (flagsHistory & LUMON_META_HIT) != 0u;
        if (curHit != histHit) {
            temporalRejectBits |= LUMON_META_TEMPREJ_HIT_CLASS_MISMATCH;
            historyValid = false;
        }
    }
    
    if (historyValid) {
        // Check if hit distance changed significantly (disocclusion)
        float maxDist = max(currentHitDist, historyHitDist);
        if (maxDist > 0.001) {
            float relativeDiff = abs(currentHitDist - historyHitDist) / maxDist;
            if (relativeDiff >= hitDistanceRejectThreshold)
            {
                temporalRejectBits |= LUMON_META_TEMPREJ_HITDIST_DELTA;
                historyValid = false;
            }
        }
    }
    
    vec4 result;
    vec2 metaOut;
    
    if (historyValid) {
        // Confidence-adaptive temporal blending
        float alpha = clamp(temporalAlpha * confHistory, 0.0, 1.0);

        // Get neighborhood bounds for clamping (prevents ghosting)
        vec4 minVal, maxVal;
        getNeighborhoodMinMax(probeCoord, octTexel, minVal, maxVal);
        
        // Clamp history to current neighborhood
        vec4 clampedHistory = clamp(history, minVal, maxVal);
        
        // Blend current with clamped history
        // Note: We blend both radiance (RGB) and hit distance (A)
        result = mix(current, clampedHistory, alpha);

        // Meta: blend confidence (flags follow current classification)
        float outConf = mix(confCurrent, confHistory, alpha);
        metaOut = lumonEncodeMeta(outConf, flagsCurrent | temporalRejectBits);
    } else {
        // Disoccluded: use current frame only (reset)
        result = current;
        metaOut = lumonEncodeMeta(confCurrent, flagsCurrent | temporalRejectBits);
    }
    
    outRadiance = result;
    outMeta = metaOut;
}
