#version 330 core

in vec2 uv;

// MRT outputs for temporally accumulated radiance
layout(location = 0) out vec4 outRadiance0;
layout(location = 1) out vec4 outRadiance1;

// ============================================================================
// LumOn Temporal Pass
// 
// Blends current frame radiance with history for temporal stability.
// Uses simple exponential moving average with rejection for disocclusions.
// ============================================================================

// Current frame radiance (from trace pass)
uniform sampler2D radianceCurrent0;
uniform sampler2D radianceCurrent1;

// History radiance (from previous frame)
uniform sampler2D radianceHistory0;
uniform sampler2D radianceHistory1;

// Probe anchor for validation
uniform sampler2D probeAnchorPosition;

// Matrices
uniform mat4 prevViewProjMatrix;

// Probe grid parameters
uniform vec2 probeGridSize;

// Temporal parameters
uniform float temporalAlpha;           // Blend factor (0.95 typical)
uniform float depthRejectThreshold;    // Depth discontinuity threshold
uniform float normalRejectThreshold;   // Normal angle threshold

// ============================================================================
// Main
// ============================================================================

void main(void)
{
    vec2 probeUV = uv;
    
    // Read current frame radiance
    vec4 current0 = texture(radianceCurrent0, probeUV);
    vec4 current1 = texture(radianceCurrent1, probeUV);
    
    // Read probe anchor to check validity
    vec4 anchorData = texture(probeAnchorPosition, probeUV);
    float valid = anchorData.w;
    
    // If probe is invalid, just pass through (or zero)
    if (valid < 0.5) {
        outRadiance0 = current0;
        outRadiance1 = current1;
        return;
    }
    
    // Read history
    vec4 history0 = texture(radianceHistory0, probeUV);
    vec4 history1 = texture(radianceHistory1, probeUV);
    
    // Simple temporal blend without reprojection for now
    // TODO: Implement proper reprojection using prevViewProjMatrix
    
    // Check if history is valid (non-zero DC term indicates valid data)
    float historyValid = step(0.001, abs(history0.x) + abs(history0.y) + abs(history0.z));
    
    // Blend factor: use full history weight if valid, otherwise use current only
    float blendFactor = temporalAlpha * historyValid;
    
    // Exponential moving average
    outRadiance0 = mix(current0, history0, blendFactor);
    outRadiance1 = mix(current1, history1, blendFactor);
}
