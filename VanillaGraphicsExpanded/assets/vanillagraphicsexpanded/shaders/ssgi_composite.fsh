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

// Spatial blur with depth-aware weighting to spread SSGI samples
// This smooths out the pinpoint samples while respecting geometry edges
vec3 spatialBlur(vec2 uv, float centerDepth, vec3 centerNormal) {
    vec2 texelSize = 1.0 / ssgiBufferSize;
    float blurRadius = 2.0; // Sample radius in texels
    
    vec3 result = vec3(0.0);
    float totalWeight = 0.0;
    
    // 3x3 blur kernel with depth/normal awareness
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            vec2 offset = vec2(float(x), float(y)) * texelSize * blurRadius;
            vec2 sampleUV = uv + offset;
            
            // Sample SSGI
            vec3 sampleGI = texture(ssgiTexture, sampleUV).rgb;
            
            // Sample depth and normal for edge detection
            float sampleDepthRaw = texture(primaryDepth, sampleUV).r;
            if (sampleDepthRaw >= 1.0) continue; // Skip sky
            
            float sampleDepth = linearizeDepth(sampleDepthRaw);
            vec3 sampleNormal = texture(gBufferNormal, sampleUV).rgb * 2.0 - 1.0;
            
            // Gaussian weight based on distance
            float dist = length(vec2(x, y));
            float gaussWeight = exp(-dist * dist / 2.0);
            
            // Depth weight: reject samples with very different depth
            float depthDiff = abs(sampleDepth - centerDepth);
            float depthWeight = exp(-depthDiff * depthDiff / (centerDepth * 0.1));
            
            // Normal weight: reject samples with different facing
            float normalDot = max(0.0, dot(normalize(sampleNormal), normalize(centerNormal)));
            float normalWeight = pow(normalDot, 4.0);
            
            float weight = gaussWeight * depthWeight * normalWeight;
            result += sampleGI * weight;
            totalWeight += weight;
        }
    }
    
    if (totalWeight > 0.0) {
        return result / totalWeight;
    }
    return texture(ssgiTexture, uv).rgb;
}

// Bilateral upscale: samples low-res SSGI using depth/normal-aware weighting
vec3 bilateralUpsample(vec2 fullResUV, float centerDepth, vec3 centerNormal) {
    // If running at full resolution, apply spatial blur directly
    if (resolutionScale >= 0.99) {
        return spatialBlur(fullResUV, centerDepth, centerNormal);
    }
    
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
        return spatialBlur(fullResUV, centerDepth, centerNormal);
    }
    
    // Apply spatial blur to the upsampled result for smoother appearance
    vec3 upsampled = result / totalWeight;
    return upsampled;
}

// ============================================================================
// Main
// ============================================================================

void main() {
    // Debug mode 1: Show raw SSGI buffer across entire screen (no filtering)
    if (debugMode == 1) {
        // Direct sample from SSGI texture - shows exactly what the SSGI pass produced
        vec3 ssgi = texture(ssgiTexture, uv).rgb;
        outColor = vec4(ssgi * 2.0, 1.0); // Boosted 2x for visibility
        return;
    }
    
    // Sample scene color
    vec3 sceneColor = texture(primaryScene, uv).rgb;
    
    // Debug mode 2: Scene only (no SSGI)
    if (debugMode == 2) {
        outColor = vec4(sceneColor, 1.0);
        return;
    }
    
    // Check if this pixel has valid G-buffer data
    vec4 normalSample = texture(gBufferNormal, uv);
    float depth = texture(primaryDepth, uv).r;
    
    // Sky/background - no SSGI contribution
    if (normalSample.a <= 0.0 || depth >= 1.0) {
        outColor = vec4(sceneColor, 1.0);
        return;
    }
    
    // Prepare center depth and normal for bilateral filtering
    float centerDepth = linearizeDepth(depth);
    vec3 centerNormal = normalSample.rgb * 2.0 - 1.0;
    
    // Get SSGI with bilateral upscaling and spatial blur
    vec3 ssgi = bilateralUpsample(uv, centerDepth, centerNormal);
    
    // Composite: add indirect lighting to scene
    // The SSGI term already has SSAO multiplied in, so we just add it
    vec3 finalColor = sceneColor + ssgi;
    
    outColor = vec4(finalColor, 1.0);
}
