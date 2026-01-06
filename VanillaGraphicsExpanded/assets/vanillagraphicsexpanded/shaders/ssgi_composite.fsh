#version 330 core

in vec2 uv;
out vec4 outColor;

// ============================================================================
// SSGI Composite Shader
//
// Blends SSGI indirect lighting into the scene.
// Performs bilateral upscaling when SSGI is rendered at lower resolution,
// using depth and normal information to preserve sharp edges.
// ============================================================================

// Textures
uniform sampler2D primaryScene;    // Full-resolution scene color
uniform sampler2D ssgiTexture;     // SSGI indirect lighting (may be lower res)
uniform sampler2D primaryDepth;    // Full-resolution depth for bilateral upscale
uniform sampler2D gBufferNormal;   // Full-resolution normals for bilateral upscale

// Uniforms
uniform vec2 frameSize;            // Full-resolution frame size
uniform vec2 ssgiBufferSize;       // SSGI buffer size (may be smaller)
uniform float resolutionScale;     // Resolution scale (0.25 - 1.0)
uniform float zNear;
uniform float zFar;

// Debug mode:
// 0 = Normal composite
// 1 = SSGI only
// 2 = Scene only (no SSGI)
uniform int debugMode;

// ============================================================================
// Bilateral Upscaling
// ============================================================================

// Linearize depth for comparison
float linearizeDepth(float depth) {
    float z = depth * 2.0 - 1.0;
    return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
}

// Bilateral upscale: samples low-res SSGI using depth/normal-aware weighting
vec3 bilateralUpsample(vec2 fullResUV) {
    // If running at full resolution, just sample directly
    if (resolutionScale >= 0.99) {
        return texture(ssgiTexture, fullResUV).rgb;
    }
    
    // Get full-res depth and normal for reference
    float centerDepth = linearizeDepth(texture(primaryDepth, fullResUV).r);
    vec3 centerNormal = texture(gBufferNormal, fullResUV).rgb * 2.0 - 1.0;
    
    // Sample offsets for bilateral filter (2x2 bilinear neighborhood in low-res)
    vec2 lowResTexelSize = 1.0 / ssgiBufferSize;
    
    // Calculate the position in the low-res texture
    vec2 lowResUV = fullResUV;
    vec2 lowResPixel = lowResUV * ssgiBufferSize;
    vec2 lowResPixelFloor = floor(lowResPixel - 0.5) + 0.5;
    vec2 lowResPixelFrac = lowResPixel - lowResPixelFloor;
    
    // Sample the four nearest low-res texels
    vec2 offsets[4];
    offsets[0] = vec2(0.0, 0.0);
    offsets[1] = vec2(1.0, 0.0);
    offsets[2] = vec2(0.0, 1.0);
    offsets[3] = vec2(1.0, 1.0);
    
    // Bilinear weights
    float weights[4];
    weights[0] = (1.0 - lowResPixelFrac.x) * (1.0 - lowResPixelFrac.y);
    weights[1] = lowResPixelFrac.x * (1.0 - lowResPixelFrac.y);
    weights[2] = (1.0 - lowResPixelFrac.x) * lowResPixelFrac.y;
    weights[3] = lowResPixelFrac.x * lowResPixelFrac.y;
    
    vec3 result = vec3(0.0);
    float totalWeight = 0.0;
    
    // Depth and normal thresholds for bilateral filtering
    float depthThreshold = centerDepth * 0.02; // 2% of depth
    float normalThreshold = 0.9; // cosine threshold (~25 degrees)
    
    for (int i = 0; i < 4; i++) {
        vec2 sampleUV = (lowResPixelFloor + offsets[i]) * lowResTexelSize;
        
        // Sample low-res SSGI and corresponding full-res depth/normal
        vec3 sampleGI = texture(ssgiTexture, sampleUV).rgb;
        
        // Map low-res UV back to full-res for depth/normal sampling
        float sampleDepth = linearizeDepth(texture(primaryDepth, sampleUV).r);
        vec3 sampleNormal = texture(gBufferNormal, sampleUV).rgb * 2.0 - 1.0;
        
        // Compute bilateral weights
        float depthDiff = abs(sampleDepth - centerDepth);
        float depthWeight = depthDiff < depthThreshold ? 1.0 : 0.0;
        
        float normalDot = dot(normalize(sampleNormal), normalize(centerNormal));
        float normalWeight = normalDot > normalThreshold ? 1.0 : 0.0;
        
        // Combined weight
        float bilateralWeight = weights[i] * depthWeight * normalWeight;
        
        result += sampleGI * bilateralWeight;
        totalWeight += bilateralWeight;
    }
    
    // Fallback to bilinear if all bilateral weights are zero
    if (totalWeight < 0.001) {
        return texture(ssgiTexture, fullResUV).rgb;
    }
    
    return result / totalWeight;
}

// ============================================================================
// Main
// ============================================================================

void main() {
    // Sample scene color
    vec3 sceneColor = texture(primaryScene, uv).rgb;
    
    // Check if this pixel has valid G-buffer data
    vec4 normalSample = texture(gBufferNormal, uv);
    float depth = texture(primaryDepth, uv).r;
    
    // Sky/background - no SSGI contribution
    if (normalSample.a <= 0.0 || depth >= 1.0) {
        outColor = vec4(sceneColor, 1.0);
        return;
    }
    
    // Get SSGI with bilateral upscaling
    vec3 ssgi = bilateralUpsample(uv);
    
    // Debug modes
    if (debugMode == 1) {
        // SSGI only
        outColor = vec4(ssgi, 1.0);
        return;
    }
    else if (debugMode == 2) {
        // Scene only (no SSGI)
        outColor = vec4(sceneColor, 1.0);
        return;
    }
    
    // Composite: add indirect lighting to scene
    // The SSGI term already has SSAO multiplied in, so we just add it
    vec3 finalColor = sceneColor + ssgi;
    
    outColor = vec4(finalColor, 1.0);
}
