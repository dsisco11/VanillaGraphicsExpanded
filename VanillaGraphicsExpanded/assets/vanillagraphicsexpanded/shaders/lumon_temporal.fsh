#version 330 core

// MRT outputs for temporally accumulated radiance
layout(location = 0) out vec4 outRadiance0;
layout(location = 1) out vec4 outRadiance1;
layout(location = 2) out vec4 outMeta;  // Depth, normal.xy, accumCount

// ============================================================================
// LumOn Temporal Pass
// 
// Blends current frame radiance with history for temporal stability.
// Implements reprojection, validation, and neighborhood clamping.
// See: LumOn.05-Temporal.md
// ============================================================================

// Current frame radiance (from trace pass)
uniform sampler2D radianceCurrent0;
uniform sampler2D radianceCurrent1;

// History radiance (from previous frame)
uniform sampler2D radianceHistory0;
uniform sampler2D radianceHistory1;

// Probe anchor for validation (world-space for temporal stability)
uniform sampler2D probeAnchorPosition;  // posWS.xyz, valid
uniform sampler2D probeAnchorNormal;    // normalWS.xyz, reserved

// History metadata (depth, normal, accumCount)
uniform sampler2D historyMeta;

// Full-resolution velocity texture (RGBA32F): RG=velocityUv, A=uintBitsToFloat(flags)
uniform sampler2D velocityTex;

// Matrices
uniform mat4 viewMatrix;              // Current frame view (for WS to VS)
uniform mat4 invViewMatrix;           // Current frame inverse view
uniform mat4 prevViewProjMatrix;      // Previous frame view-projection

// Probe grid parameters
uniform vec2 probeGridSize;

// Screen mapping + deterministic jitter replication (must match probe anchor pass)
uniform vec2 screenSize;
uniform int probeSpacing;
uniform int frameIndex;
uniform int anchorJitterEnabled;
uniform float anchorJitterScale;

// Depth parameters
uniform float zNear;
uniform float zFar;

// Temporal parameters
uniform float temporalAlpha;           // Blend factor (0.95 typical)
uniform float depthRejectThreshold;    // Depth discontinuity threshold
uniform float normalRejectThreshold;   // Normal angle threshold (dot product)

// Phase 14 reprojection integration
uniform int enableReprojectionVelocity;
uniform float velocityRejectThreshold;

// Deterministic jitter hash (already used in probe anchor)
@import "./includes/squirrel3.glsl"
@import "./includes/velocity_common.glsl"

// ============================================================================
// Helper Functions
// ============================================================================

/// Linearize depth from hyperbolic depth buffer value
float LinearizeDepth(float d) {
    return zNear * zFar / (zFar - d * (zFar - zNear));
}

/// Convert world-space position to previous frame screen UV
vec2 ReprojectToHistory(vec3 posWS) {
    // World-space directly to previous clip-space (no VS conversion needed)
    vec4 prevClip = prevViewProjMatrix * vec4(posWS, 1.0);

    // Reject if behind camera (prevents invalid NDC mapping from negative w)
    if (prevClip.w <= 1e-8) {
        return vec2(-1.0);
    }
    
    // Clip to NDC (perspective divide)
    vec3 prevNDC = prevClip.xyz / prevClip.w;
    
    // NDC to UV [0,1]
    return prevNDC.xy * 0.5 + 0.5;
}

vec2 ComputeProbeScreenUv(ivec2 probeCoord)
{
    vec2 baseUv = (vec2(probeCoord) + 0.5) * float(probeSpacing) / screenSize;

    if (anchorJitterEnabled != 0 && anchorJitterScale > 0.0) {
        float u1 = Squirrel3HashF(probeCoord.x, probeCoord.y, frameIndex * 2);
        float u2 = Squirrel3HashF(probeCoord.x, probeCoord.y, frameIndex * 2 + 1);
        vec2 jitter = vec2(u1, u2) - vec2(0.5);

        float maxOffsetPx = float(probeSpacing) * anchorJitterScale;
        vec2 jitterUv = (jitter * maxOffsetPx) / screenSize;

        vec2 uvPad = vec2(0.5) / screenSize;
        baseUv = clamp(baseUv + jitterUv, uvPad, vec2(1.0) - uvPad);
    }

    return baseUv;
}

// ============================================================================
// Validation
// ============================================================================

struct ValidationResult {
    bool valid;
    float confidence;  // 0-1, how much to trust history
};

vec3 ReconstructHistoryNormal(vec2 historyNormal2D, vec3 currentNormal) {
    float z2 = max(1.0 - dot(historyNormal2D, historyNormal2D), 0.0);
    float z = sqrt(z2);
    float zSign = (currentNormal.z >= 0.0) ? 1.0 : -1.0;
    return normalize(vec3(historyNormal2D, z * zSign));
}

ValidationResult ValidateHistory(vec2 historyUV, float currentDepthLin, vec3 currentNormal) {
    ValidationResult result;
    result.valid = false;
    result.confidence = 0.0;
    
    // Bounds check
    if (historyUV.x < 0.0 || historyUV.x > 1.0 ||
        historyUV.y < 0.0 || historyUV.y > 1.0) {
        return result;
    }
    
    // Sample history metadata
    // Layout: R = depth, G = normal.x encoded, B = normal.y encoded, A = accumCount
    // Note: Only 2D normal is stored (X, Y in GB channels), A contains accumCount
    vec4 histMeta = texture(historyMeta, historyUV);
    float historyDepthLin = histMeta.r;
    vec2 historyNormal2D = histMeta.gb * 2.0 - 1.0;  // Only X and Y components
    
    // Skip validation if history has no valid data (depth = 0)
    if (historyDepthLin < 0.001) {
        return result;
    }
    
    // Depth rejection: relative difference
    float depthDiff = abs(currentDepthLin - historyDepthLin) / max(currentDepthLin, 0.001);
    if (depthDiff > depthRejectThreshold) {
        return result;
    }
    
    // Normal rejection: reconstruct Z from stored XY and compare in 3D.
    // This avoids rejecting normals where XY is near zero (e.g. (0,0,-1)).
    vec3 historyNormal = ReconstructHistoryNormal(historyNormal2D, currentNormal);
    float normalDot = dot(normalize(currentNormal), historyNormal);
    if (normalDot < normalRejectThreshold) {
        return result;
    }
    
    result.valid = true;
    
    // Compute confidence based on how close we are to thresholds
    float depthConf = 1.0 - (depthDiff / depthRejectThreshold);
    float normalConf = (normalDot - normalRejectThreshold) / (1.0 - normalRejectThreshold);
    result.confidence = clamp(min(depthConf, normalConf), 0.0, 1.0);
    
    return result;
}

// ============================================================================
// Neighborhood Clamping
// ============================================================================

void GetNeighborhoodMinMax(ivec2 probeCoord,
                           out vec4 minVal0, out vec4 maxVal0,
                           out vec4 minVal1, out vec4 maxVal1) {
    minVal0 = vec4(1e10);
    maxVal0 = vec4(-1e10);
    minVal1 = vec4(1e10);
    maxVal1 = vec4(-1e10);
    
    ivec2 gridSize = ivec2(probeGridSize);
    
    for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
            ivec2 neighbor = clamp(probeCoord + ivec2(dx, dy),
                                   ivec2(0), gridSize - 1);
            
            vec4 s0 = texelFetch(radianceCurrent0, neighbor, 0);
            vec4 s1 = texelFetch(radianceCurrent1, neighbor, 0);
            
            minVal0 = min(minVal0, s0);
            maxVal0 = max(maxVal0, s0);
            minVal1 = min(minVal1, s1);
            maxVal1 = max(maxVal1, s1);
        }
    }
}

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    ivec2 probeCoord = ivec2(gl_FragCoord.xy);
    
    // Load current frame data
    vec4 currentRad0 = texelFetch(radianceCurrent0, probeCoord, 0);
    vec4 currentRad1 = texelFetch(radianceCurrent1, probeCoord, 0);
    
    // Read probe anchor data (world-space for temporal stability)
    vec4 anchorPos = texelFetch(probeAnchorPosition, probeCoord, 0);
    vec4 anchorNormal = texelFetch(probeAnchorNormal, probeCoord, 0);
    
    vec3 posWS = anchorPos.xyz;
    float valid = anchorPos.w;
    vec3 normalWS = normalize(anchorNormal.xyz * 2.0 - 1.0);
    
    // Invalid probe: pass through current frame data
    if (valid < 0.5) {
        outRadiance0 = currentRad0;
        outRadiance1 = currentRad1;
        outMeta = vec4(0.0);
        return;
    }
    
    // Convert to view-space for depth calculation
    vec3 posVS = (viewMatrix * vec4(posWS, 1.0)).xyz;

    // Convert normal to view-space for validation
    vec3 normalVS = normalize(mat3(viewMatrix) * normalWS);
    
    // Compute linearized depth for validation
    // View-space Z is negative (looking down -Z), so negate
    float currentDepthLin = -posVS.z;
    
    // Reproject to history UV
    // Default: world-space anchor reprojection.
    // Optional: velocity-based reprojection using the per-pixel velocity buffer.
    vec2 historyUV = ReprojectToHistory(posWS);

    float motionWeight = 1.0;

    if (enableReprojectionVelocity != 0) {
        vec2 screenUv = ComputeProbeScreenUv(probeCoord);
        vec4 velSample = texture(velocityTex, screenUv);
        vec2 velocityUv = lumonVelocityDecodeUv(velSample);
        uint velFlags = lumonVelocityDecodeFlags(velSample);

        if (!lumonVelocityIsValid(velFlags)) {
            historyUV = vec2(-1.0);
        } else {
            // velocityUv = currUv - prevUv  =>  prevUv = currUv - velocityUv
            vec2 prevUv = screenUv - velocityUv;
            historyUV = prevUv;

            float velMag = lumonVelocityMagnitude(velocityUv);
            if (velocityRejectThreshold > 0.0) {
                // Reject history past threshold; otherwise down-weight as motion increases.
                if (velMag > velocityRejectThreshold) {
                    historyUV = vec2(-1.0);
                } else {
                    motionWeight = clamp(1.0 - velMag / velocityRejectThreshold, 0.0, 1.0);
                }
            }
        }
    }
    
    // Validate history sample
    ValidationResult validation = ValidateHistory(historyUV, currentDepthLin, normalVS);
    
    vec4 outputRad0;
    vec4 outputRad1;
    float accumCount = 1.0;
    
    if (validation.valid) {
        // Sample history radiance
        vec4 historyRad0 = texture(radianceHistory0, historyUV);
        vec4 historyRad1 = texture(radianceHistory1, historyUV);
        
        // Get neighborhood bounds for clamping
        vec4 minVal0, maxVal0, minVal1, maxVal1;
        GetNeighborhoodMinMax(probeCoord, minVal0, maxVal0, minVal1, maxVal1);
        
        // Clamp history to neighborhood (prevents ghosting)
        historyRad0 = clamp(historyRad0, minVal0, maxVal0);
        historyRad1 = clamp(historyRad1, minVal1, maxVal1);
        
        // Adaptive blend based on validation confidence
        float alpha = temporalAlpha * validation.confidence * motionWeight;
        
        // Edge probes get less temporal accumulation (more unstable)
        if (valid < 0.9) {
            alpha *= 0.5;
        }
        
        // Ramp up alpha as we accumulate more frames (Section 6.3)
        // First few frames use more current data to converge faster
        float prevAccum = texture(historyMeta, historyUV).a;
        alpha *= min(prevAccum / 10.0, 1.0);
        
        // Exponential moving average
        outputRad0 = mix(currentRad0, historyRad0, alpha);
        outputRad1 = mix(currentRad1, historyRad1, alpha);
        
        // Track accumulation count
        accumCount = min(prevAccum + 1.0, 100.0);  // Cap at 100 frames
    } else {
        // Disoccluded: use current frame only (reset)
        outputRad0 = currentRad0;
        outputRad1 = currentRad1;
        accumCount = 1.0;
    }
    
    outRadiance0 = outputRad0;
    outRadiance1 = outputRad1;
    
    // Store metadata for next frame validation
    // Layout: R = linearized depth, G = normal.x encoded, B = normal.y encoded, A = accumCount
    // Note: Only 2D normal stored since A is used for accumCount
    outMeta = vec4(currentDepthLin, normalVS.xy * 0.5 + 0.5, accumCount);
}
