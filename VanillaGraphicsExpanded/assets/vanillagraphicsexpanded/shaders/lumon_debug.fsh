#version 330 core

in vec2 uv;

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
// ============================================================================

// Import common utilities
@import "lumon_common.fsh"

// G-buffer textures
uniform sampler2D primaryDepth;
uniform sampler2D gBufferNormal;

// Probe textures
uniform sampler2D probeAnchorPosition;  // posVS.xyz, valid
uniform sampler2D probeAnchorNormal;
uniform sampler2D radianceTexture0;
uniform sampler2D indirectHalf;

// Temporal textures
uniform sampler2D historyMeta;          // linearized depth, normal, accumCount

// Matrices
uniform mat4 invProjectionMatrix;

// Size uniforms
uniform vec2 screenSize;
uniform vec2 probeGridSize;
uniform int probeSpacing;

// Z-planes
uniform float zNear;
uniform float zFar;

// Temporal config
uniform float temporalAlpha;
uniform float depthRejectThreshold;
uniform float normalRejectThreshold;

// Matrices for reprojection
uniform mat4 invViewMatrix;
uniform mat4 prevViewProjMatrix;

// Debug mode
uniform int debugMode;

// ============================================================================
// Color Utilities
// ============================================================================

vec3 heatmap(float t) {
    // Blue -> Cyan -> Green -> Yellow -> Red
    t = clamp(t, 0.0, 1.0);
    vec3 c;
    if (t < 0.25) {
        c = mix(vec3(0.0, 0.0, 1.0), vec3(0.0, 1.0, 1.0), t * 4.0);
    } else if (t < 0.5) {
        c = mix(vec3(0.0, 1.0, 1.0), vec3(0.0, 1.0, 0.0), (t - 0.25) * 4.0);
    } else if (t < 0.75) {
        c = mix(vec3(0.0, 1.0, 0.0), vec3(1.0, 1.0, 0.0), (t - 0.5) * 4.0);
    } else {
        c = mix(vec3(1.0, 1.0, 0.0), vec3(1.0, 0.0, 0.0), (t - 0.75) * 4.0);
    }
    return c;
}

// ============================================================================
// Debug Mode 1: Probe Grid Visualization
// ============================================================================

vec4 renderProbeGridDebug(vec2 screenPos) {
    // Sample the scene as background
    float depth = texture(primaryDepth, uv).r;
    vec3 baseColor = vec3(0.1);
    
    if (!lumonIsSky(depth)) {
        // Show darkened scene as background
        vec3 normal = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);
        baseColor = normal * 0.3 + 0.2;
    }
    
    // Calculate which probe cell this pixel is in
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    
    // Calculate the center of this probe cell in screen space
    vec2 probeCenter = (vec2(probeCoord) + 0.5) * float(probeSpacing);
    
    // Distance from pixel to probe center
    float dist = length(screenPos - probeCenter);
    
    // Probe dot radius
    float dotRadius = max(3.0, float(probeSpacing) * 0.25);
    
    // Draw probe dots
    if (dist < dotRadius) {
        // Clamp probe coord to valid range
        if (probeCoord.x >= 0 && probeCoord.y >= 0 && 
            probeCoord.x < int(probeGridSize.x) && probeCoord.y < int(probeGridSize.y)) {
            
            // Sample probe validity from anchor texture
            vec4 probeData = texelFetch(probeAnchorPosition, probeCoord, 0);
            float valid = probeData.a;
            
            // Color by validity
            vec3 probeColor;
            if (valid > 0.9) {
                probeColor = vec3(0.0, 1.0, 0.0);  // Green = fully valid
            } else if (valid > 0.4) {
                probeColor = vec3(1.0, 1.0, 0.0);  // Yellow = edge (partial validity)
            } else {
                probeColor = vec3(1.0, 0.0, 0.0);  // Red = invalid
            }
            
            // Smooth edge falloff
            float alpha = smoothstep(dotRadius, dotRadius * 0.5, dist);
            
            return vec4(mix(baseColor, probeColor, alpha), 1.0);
        }
    }
    
    // Draw grid lines between probes
    vec2 gridPos = mod(screenPos, float(probeSpacing));
    float lineWidth = 1.0;
    if (gridPos.x < lineWidth || gridPos.y < lineWidth) {
        return vec4(mix(baseColor, vec3(0.5), 0.4), 1.0);
    }
    
    return vec4(baseColor, 1.0);
}

// ============================================================================
// Debug Mode 2: Probe Depth Heatmap
// ============================================================================

vec4 renderProbeDepthDebug(vec2 screenPos) {
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    probeCoord = clamp(probeCoord, ivec2(0), ivec2(probeGridSize) - 1);
    
    vec4 probeData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = probeData.a;
    
    if (valid < 0.1) {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black for invalid
    }
    
    // View-space Z is negative, closer to camera is smaller abs value
    float probeDepth = -probeData.z;
    
    // Normalize to reasonable range (0-100m)
    float normalizedDepth = probeDepth / 100.0;
    
    return vec4(heatmap(normalizedDepth), 1.0);
}

// ============================================================================
// Debug Mode 3: Probe Normals
// ============================================================================

vec4 renderProbeNormalDebug(vec2 screenPos) {
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    probeCoord = clamp(probeCoord, ivec2(0), ivec2(probeGridSize) - 1);
    
    vec4 posData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = posData.a;
    
    if (valid < 0.1) {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black for invalid
    }
    
    vec3 probeNormal = texelFetch(probeAnchorNormal, probeCoord, 0).xyz;
    // Normal is stored encoded [0,1], display as-is (already visualizes nicely)
    return vec4(probeNormal, 1.0);
}

// ============================================================================
// Debug Mode 4: Scene Depth
// ============================================================================

vec4 renderSceneDepthDebug() {
    float depth = texture(primaryDepth, uv).r;
    
    if (lumonIsSky(depth)) {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }
    
    float linearDepth = lumonLinearizeDepth(depth, zNear, zFar);
    float normalizedDepth = linearDepth / 100.0;  // Normalize to ~100m
    
    return vec4(heatmap(normalizedDepth), 1.0);
}

// ============================================================================
// Debug Mode 5: Scene Normals
// ============================================================================

vec4 renderSceneNormalDebug() {
    float depth = texture(primaryDepth, uv).r;
    
    if (lumonIsSky(depth)) {
        return vec4(0.5, 0.5, 1.0, 1.0);  // Sky blue for no geometry
    }
    
    vec3 normal = texture(gBufferNormal, uv).xyz;
    return vec4(normal, 1.0);  // Already encoded [0,1]
}

// ============================================================================
// Debug Mode 6: Temporal Weight
// ============================================================================

/// Reproject view-space position to previous frame UV
vec2 reprojectToHistory(vec3 posVS) {
    vec4 posWS = invViewMatrix * vec4(posVS, 1.0);
    vec4 prevClip = prevViewProjMatrix * posWS;
    vec3 prevNDC = prevClip.xyz / prevClip.w;
    return prevNDC.xy * 0.5 + 0.5;
}

vec4 renderTemporalWeightDebug(vec2 screenPos) {
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    probeCoord = clamp(probeCoord, ivec2(0), ivec2(probeGridSize) - 1);
    
    vec4 posData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = posData.a;
    
    if (valid < 0.1) {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black for invalid probes
    }
    
    vec3 posVS = posData.xyz;
    vec3 normalVS = normalize(texelFetch(probeAnchorNormal, probeCoord, 0).xyz * 2.0 - 1.0);
    float currentDepthLin = -posVS.z;
    
    // Reproject to history UV
    vec2 historyUV = reprojectToHistory(posVS);
    
    // Check bounds
    if (historyUV.x < 0.0 || historyUV.x > 1.0 ||
        historyUV.y < 0.0 || historyUV.y > 1.0) {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black = out of bounds
    }
    
    // Sample history metadata
    vec4 histMeta = texture(historyMeta, historyUV);
    float historyDepthLin = histMeta.r;
    vec3 historyNormal = histMeta.gba * 2.0 - 1.0;
    
    if (historyDepthLin < 0.001) {
        return vec4(0.0, 0.0, 0.0, 1.0);  // No valid history
    }
    
    // Compute validation confidence
    float depthDiff = abs(currentDepthLin - historyDepthLin) / max(currentDepthLin, 0.001);
    float normalDot = dot(normalize(normalVS), normalize(historyNormal));
    
    if (depthDiff > depthRejectThreshold || normalDot < normalRejectThreshold) {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Rejected
    }
    
    float depthConf = 1.0 - (depthDiff / depthRejectThreshold);
    float normalConf = (normalDot - normalRejectThreshold) / (1.0 - normalRejectThreshold);
    float confidence = clamp(min(depthConf, normalConf), 0.0, 1.0);
    
    float weight = temporalAlpha * confidence;
    if (valid < 0.9) weight *= 0.5;  // Edge probe penalty
    
    // Grayscale: brighter = more history used
    return vec4(weight, weight, weight, 1.0);
}

// ============================================================================
// Debug Mode 7: Temporal Rejection Mask
// ============================================================================

vec4 renderTemporalRejectionDebug(vec2 screenPos) {
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    probeCoord = clamp(probeCoord, ivec2(0), ivec2(probeGridSize) - 1);
    
    vec4 posData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = posData.a;
    
    if (valid < 0.1) {
        return vec4(0.2, 0.2, 0.2, 1.0);  // Dark gray for invalid probes
    }
    
    vec3 posVS = posData.xyz;
    vec3 normalVS = normalize(texelFetch(probeAnchorNormal, probeCoord, 0).xyz * 2.0 - 1.0);
    float currentDepthLin = -posVS.z;
    
    // Reproject to history UV
    vec2 historyUV = reprojectToHistory(posVS);
    
    // Check bounds
    if (historyUV.x < 0.0 || historyUV.x > 1.0 ||
        historyUV.y < 0.0 || historyUV.y > 1.0) {
        return vec4(1.0, 0.0, 0.0, 1.0);  // Red = out of bounds
    }
    
    // Sample history metadata
    vec4 histMeta = texture(historyMeta, historyUV);
    float historyDepthLin = histMeta.r;
    vec3 historyNormal = histMeta.gba * 2.0 - 1.0;
    
    if (historyDepthLin < 0.001) {
        return vec4(0.5, 0.0, 0.5, 1.0);  // Purple = no history data
    }
    
    // Check depth rejection
    float depthDiff = abs(currentDepthLin - historyDepthLin) / max(currentDepthLin, 0.001);
    if (depthDiff > depthRejectThreshold) {
        return vec4(1.0, 1.0, 0.0, 1.0);  // Yellow = depth reject
    }
    
    // Check normal rejection
    float normalDot = dot(normalize(normalVS), normalize(historyNormal));
    if (normalDot < normalRejectThreshold) {
        return vec4(1.0, 0.5, 0.0, 1.0);  // Orange = normal reject
    }
    
    // Valid history
    return vec4(0.0, 1.0, 0.0, 1.0);  // Green = valid
}

// ============================================================================
// Main
// ============================================================================

void main(void)
{
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
        default:
            outColor = vec4(1.0, 0.0, 1.0, 1.0);  // Magenta = unknown mode
            break;
    }
}
