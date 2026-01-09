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
// ============================================================================

// Import common utilities
@import "lumon_common.fsh"

// Import SH helpers for mode 8
@import "lumon_sh.fsh"

// G-buffer textures
uniform sampler2D primaryDepth;
uniform sampler2D gBufferNormal;

// Probe textures
uniform sampler2D probeAnchorPosition;  // posWS.xyz, valid
uniform sampler2D probeAnchorNormal;
uniform sampler2D radianceTexture0;
uniform sampler2D radianceTexture1;     // Second SH texture for full unpacking
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

mat4 getViewMatrix() {
    return inverse(invViewMatrix);
}

vec3 worldToViewPos(vec3 posWS) {
    return (getViewMatrix() * vec4(posWS, 1.0)).xyz;
}

vec3 reconstructHistoryNormal(vec2 historyNormal2D, vec3 currentNormal) {
    float z2 = max(1.0 - dot(historyNormal2D, historyNormal2D), 0.0);
    float z = sqrt(z2);
    float zSign = (currentNormal.z >= 0.0) ? 1.0 : -1.0;
    return normalize(vec3(historyNormal2D, z * zSign));
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
    
    // Probe anchors are in world-space; compute view-space depth.
    float probeDepth = -worldToViewPos(probeData.xyz).z;
    
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
    
    // Decode normal from [0,1] to [-1,1], then re-encode for visualization
    vec3 probeNormalEncoded = texelFetch(probeAnchorNormal, probeCoord, 0).xyz;
    vec3 probeNormalDecoded = lumonDecodeNormal(probeNormalEncoded);
    // Display as color: remap [-1,1] to [0,1] so all directions are visible
    return vec4(probeNormalDecoded * 0.5 + 0.5, 1.0);
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
    
    // Decode normal from G-buffer [0,1] to [-1,1], then re-encode for visualization
    vec3 normalEncoded = texture(gBufferNormal, uv).xyz;
    vec3 normalDecoded = lumonDecodeNormal(normalEncoded);
    // Display as color: remap [-1,1] to [0,1] so all directions are visible
    return vec4(normalDecoded * 0.5 + 0.5, 1.0);
}

// ============================================================================
// Debug Mode 6: Temporal Weight
// ============================================================================

/// Reproject world-space position to previous frame UV
vec2 reprojectToHistory(vec3 posWS) {
    vec4 prevClip = prevViewProjMatrix * vec4(posWS, 1.0);
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
    
    vec3 posWS = posData.xyz;
    vec3 posVS = worldToViewPos(posWS);
    vec3 normalWS = lumonDecodeNormal(texelFetch(probeAnchorNormal, probeCoord, 0).xyz);
    vec3 normalVS = normalize(mat3(getViewMatrix()) * normalWS);
    float currentDepthLin = -posVS.z;
    
    // Reproject to history UV
    vec2 historyUV = reprojectToHistory(posWS);
    
    // Check bounds
    if (historyUV.x < 0.0 || historyUV.x > 1.0 ||
        historyUV.y < 0.0 || historyUV.y > 1.0) {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black = out of bounds
    }
    
    // Sample history metadata
    // Layout (matches lumon_temporal.fsh):
    // R = linearDepth, G = normal.x encoded, B = normal.y encoded, A = accumCount
    vec4 histMeta = texture(historyMeta, historyUV);
    float historyDepthLin = histMeta.r;
    vec2 historyNormal2D = histMeta.gb * 2.0 - 1.0;
    
    if (historyDepthLin < 0.001) {
        return vec4(0.0, 0.0, 0.0, 1.0);  // No valid history
    }
    
    // Compute validation confidence
    float depthDiff = abs(currentDepthLin - historyDepthLin) / max(currentDepthLin, 0.001);
    vec3 historyNormal = reconstructHistoryNormal(historyNormal2D, normalVS);
    float normalDot = dot(normalize(normalVS), historyNormal);
    
    if (depthDiff > depthRejectThreshold || normalDot < normalRejectThreshold) {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Rejected
    }
    
    float depthConf = 1.0 - (depthDiff / depthRejectThreshold);
    float normalConf = (normalDot - normalRejectThreshold) / (1.0 - normalRejectThreshold);
    float confidence = clamp(min(depthConf, normalConf), 0.0, 1.0);
    
    float weight = temporalAlpha * confidence;
    if (valid < 0.9) weight *= 0.5;  // Edge probe penalty

    // Match temporal ramp-up: early frames use less history
    float prevAccum = histMeta.a;
    weight *= min(prevAccum / 10.0, 1.0);
    
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
    
    vec3 posWS = posData.xyz;
    vec3 posVS = worldToViewPos(posWS);
    vec3 normalWS = lumonDecodeNormal(texelFetch(probeAnchorNormal, probeCoord, 0).xyz);
    vec3 normalVS = normalize(mat3(getViewMatrix()) * normalWS);
    float currentDepthLin = -posVS.z;
    
    // Reproject to history UV
    vec2 historyUV = reprojectToHistory(posWS);
    
    // Check bounds
    if (historyUV.x < 0.0 || historyUV.x > 1.0 ||
        historyUV.y < 0.0 || historyUV.y > 1.0) {
        return vec4(1.0, 0.0, 0.0, 1.0);  // Red = out of bounds
    }
    
    // Sample history metadata
    vec4 histMeta = texture(historyMeta, historyUV);
    float historyDepthLin = histMeta.r;
    vec2 historyNormal2D = histMeta.gb * 2.0 - 1.0;
    
    if (historyDepthLin < 0.001) {
        return vec4(0.5, 0.0, 0.5, 1.0);  // Purple = no history data
    }
    
    // Check depth rejection
    float depthDiff = abs(currentDepthLin - historyDepthLin) / max(currentDepthLin, 0.001);
    if (depthDiff > depthRejectThreshold) {
        return vec4(1.0, 1.0, 0.0, 1.0);  // Yellow = depth reject
    }
    
    // Check normal rejection
    vec3 historyNormal = reconstructHistoryNormal(historyNormal2D, normalVS);
    float normalDot = dot(normalize(normalVS), historyNormal);
    if (normalDot < normalRejectThreshold) {
        return vec4(1.0, 0.5, 0.0, 1.0);  // Orange = normal reject
    }
    
    // Valid history
    return vec4(0.0, 1.0, 0.0, 1.0);  // Green = valid
}

// ============================================================================
// Debug Mode 8: SH Coefficients
// Shows SH radiance data: DC (ambient) as RGB, directional magnitude as brightness
// ============================================================================

vec4 renderSHCoefficientsDebug(vec2 screenPos) {
    ivec2 probeCoord = ivec2(screenPos / float(probeSpacing));
    probeCoord = clamp(probeCoord, ivec2(0), ivec2(probeGridSize) - 1);
    
    vec4 posData = texelFetch(probeAnchorPosition, probeCoord, 0);
    float valid = posData.a;
    
    if (valid < 0.1) {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black for invalid probes
    }
    
    // Load SH data from both textures
    vec4 sh0 = texelFetch(radianceTexture0, probeCoord, 0);
    vec4 sh1 = texelFetch(radianceTexture1, probeCoord, 0);
    
    // Unpack SH coefficients
    vec4 shR, shG, shB;
    shUnpackFromTextures(sh0, sh1, shR, shG, shB);
    
    // DC terms (ambient/average radiance) - stored in first coefficient
    vec3 dc = vec3(shR.x, shG.x, shB.x);
    
    // Directional magnitude - sum of absolute values of directional coefficients
    float dirMagR = abs(shR.y) + abs(shR.z) + abs(shR.w);
    float dirMagG = abs(shG.y) + abs(shG.z) + abs(shG.w);
    float dirMagB = abs(shB.y) + abs(shB.z) + abs(shB.w);
    float dirMag = (dirMagR + dirMagG + dirMagB) / 3.0;
    
    // Visualize: DC as base color, directional as brightness boost
    vec3 color = dc + vec3(dirMag * 0.5);
    
    // Apply tone mapping for HDR values
    color = color / (color + vec3(1.0));
    
    return vec4(color, 1.0);
}

// ============================================================================
// Debug Mode 9: Interpolation Weights
// Shows which probes contribute to each pixel and their weights
// ============================================================================

vec4 renderInterpolationWeightsDebug(vec2 screenPos) {
    // Get pixel's probe-space position
    vec2 probePos = screenPos / float(probeSpacing);
    ivec2 baseProbe = ivec2(floor(probePos));
    vec2 fracCoord = fract(probePos);
    
    // Bilinear base weights
    float bw00 = (1.0 - fracCoord.x) * (1.0 - fracCoord.y);
    float bw10 = fracCoord.x * (1.0 - fracCoord.y);
    float bw01 = (1.0 - fracCoord.x) * fracCoord.y;
    float bw11 = fracCoord.x * fracCoord.y;
    
    // Load probe validity
    ivec2 p00 = clamp(baseProbe + ivec2(0, 0), ivec2(0), ivec2(probeGridSize) - 1);
    ivec2 p10 = clamp(baseProbe + ivec2(1, 0), ivec2(0), ivec2(probeGridSize) - 1);
    ivec2 p01 = clamp(baseProbe + ivec2(0, 1), ivec2(0), ivec2(probeGridSize) - 1);
    ivec2 p11 = clamp(baseProbe + ivec2(1, 1), ivec2(0), ivec2(probeGridSize) - 1);
    
    float v00 = texelFetch(probeAnchorPosition, p00, 0).a;
    float v10 = texelFetch(probeAnchorPosition, p10, 0).a;
    float v01 = texelFetch(probeAnchorPosition, p01, 0).a;
    float v11 = texelFetch(probeAnchorPosition, p11, 0).a;
    
    // Apply validity to weights
    float w00 = bw00 * (v00 > 0.5 ? 1.0 : 0.0);
    float w10 = bw10 * (v10 > 0.5 ? 1.0 : 0.0);
    float w01 = bw01 * (v01 > 0.5 ? 1.0 : 0.0);
    float w11 = bw11 * (v11 > 0.5 ? 1.0 : 0.0);
    
    float totalWeight = w00 + w10 + w01 + w11;
    
    if (totalWeight < 0.001) {
        return vec4(0.0, 0.0, 0.0, 1.0);  // Black = no valid probes
    }
    
    // Normalize weights
    w00 /= totalWeight;
    w10 /= totalWeight;
    w01 /= totalWeight;
    w11 /= totalWeight;
    
    // Visualize as color:
    // R = w00 (bottom-left, red)
    // G = w10 (bottom-right, green)  
    // B = w01 + w11 (top probes, blue)
    vec3 color = vec3(w00, w10, w01 + w11);
    
    // Also draw probe dots for reference
    vec2 probeCenter = (vec2(baseProbe) + 0.5) * float(probeSpacing);
    float dotRadius = max(2.0, float(probeSpacing) * 0.15);
    
    // Check all 4 probe positions
    for (int dy = 0; dy <= 1; dy++) {
        for (int dx = 0; dx <= 1; dx++) {
            vec2 pCenter = (vec2(baseProbe + ivec2(dx, dy)) + 0.5) * float(probeSpacing);
            float dist = length(screenPos - pCenter);
            if (dist < dotRadius) {
                // Color probe dot based on its weight
                float probeWeight = (dx == 0 && dy == 0) ? w00 :
                                    (dx == 1 && dy == 0) ? w10 :
                                    (dx == 0 && dy == 1) ? w01 : w11;
                return vec4(vec3(probeWeight), 1.0);
            }
        }
    }
    
    return vec4(color, 1.0);
}

// ============================================================================
// Debug Mode 10: Radiance Overlay
// Shows the indirect diffuse radiance buffer (half-res) as fullscreen output.
// ============================================================================

vec4 renderRadianceOverlayDebug() {
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // indirectHalf is a half-resolution HDR buffer. Sample in normalized UVs;
    // the hardware sampler handles the resolution mismatch.
    vec3 rad = texture(indirectHalf, uv).rgb;

    // Simple Reinhard tone map for visualization
    vec3 color = rad / (rad + vec3(1.0));
    return vec4(color, 1.0);
}

// ============================================================================
// Debug Mode 11: Gather Weight (Diagnostic)
// Visualizes indirectHalf alpha written by gather passes:
// - grayscale = edge-aware total weight (scaled)
// - red = fallback path used (alpha < 0)
// ============================================================================

vec4 renderGatherWeightDebug() {
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    float a = texture(indirectHalf, uv).a;
    float w = clamp(abs(a), 0.0, 1.0);
    // Slight curve to make low weights more visible
    w = sqrt(w);

    if (a < 0.0) {
        return vec4(w, 0.0, 0.0, 1.0);
    }

    return vec4(vec3(w), 1.0);
}

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
        default:
            outColor = vec4(1.0, 0.0, 1.0, 1.0);  // Magenta = unknown mode
            break;
    }
}
