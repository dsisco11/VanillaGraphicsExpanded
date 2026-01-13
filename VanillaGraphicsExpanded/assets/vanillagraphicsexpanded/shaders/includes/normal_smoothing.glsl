#ifndef VGE_NORMAL_SMOOTHING_GLSL
#define VGE_NORMAL_SMOOTHING_GLSL

// Teardown-style normal blurring using golden ratio spiral sampling
// This gives the illusion of beveled edges after shading
// Reference: https://juandiegomontoya.github.io/teardown_breakdown.html
vec3 sampleNormalSmooth(sampler2D normalTex, sampler2D depthTex, vec2 texCoord, vec2 texelSize, float centerDepth, int sampleCount, float blurRadius) {
    // Golden ratio constant for spiral sampling (Teardown-style)
    const float PI = 3.141592653589793;
    const float PHI = 1.618033988749895; // (1 + sqrt(5)) / 2
    const float TAU = 6.283185307179586; // 2 * PI
    const float GOLDEN_ANGLE = 2.399963229728653; // TAU / PHI^2 ≈ 137.5 degrees in radians
    // Sample center normal
    vec4 centerSample = texture(normalTex, texCoord);
    vec3 centerNormal = (centerSample.rgb * 2.0) - 1.0;
    
    // Early out if blur is disabled or no valid normal data
    if (sampleCount <= 0 || blurRadius <= 0.0 || centerSample.a <= 0.0) {
        return normalize(centerNormal);
    }
    
    // Linearize center depth for comparison
    float centerLinearDepth = linearizeDepth(centerDepth);
    
    // Depth threshold: reject samples from different objects
    // More generous threshold allows smoother blending across nearby surfaces
    float depthThreshold = max(0.01, centerLinearDepth * 0.03); // ~3% of depth, min 0.01 blocks
    // float depthThreshold = (centerLinearDepth * 0.03); // ~3% of depth, min 0.15 blocks
    
    // Accumulate weighted normals - start with lower center weight for more averaging
    float totalWeight = 0.5;
    vec3 accumulatedNormal = centerNormal * totalWeight;
    
    // Gaussian sigma for spatial falloff - larger sigma = smoother result
    float sigma = blurRadius * 0.6; // Wider Gaussian for smoother falloff
    float sigma2 = sigma * sigma;
    
    // Golden ratio spiral sampling pattern (Vogel disk / sunflower pattern)
    // Each sample: angle = i * GOLDEN_ANGLE, radius = sqrt(i / N) * blurRadius
    for (int i = 1; i <= sampleCount; i++) {
        // Calculate spiral position using golden angle
        float angle = float(i) * GOLDEN_ANGLE;
        // Use a more uniform distribution by adjusting the power
        float t = float(i) / float(sampleCount);
        float radius = sqrt(t) * blurRadius;
        
        // Offset in pixels, then convert to UV space
        vec2 offset = vec2(cos(angle), sin(angle)) * radius * texelSize;
        vec2 sampleUV = texCoord + offset;
        
        // Sample normal and depth at this location
        vec4 sampleNormal = texture(normalTex, sampleUV);
        float sampleDepth = texture(depthTex, sampleUV).r;
        
        // Skip invalid samples (no G-buffer data)
        if (sampleNormal.a <= 0.0) continue;
        
        // Decode sampled normal
        vec3 decodedNormal = (sampleNormal.rgb * 2.0) - 1.0;
        
        // Linearize sample depth
        float sampleLinearDepth = linearizeDepth(sampleDepth);
        
        // Soft depth weight using smooth falloff instead of hard cutoff
        // This creates smoother transitions at depth boundaries
        float depthDiff = abs(sampleLinearDepth - centerLinearDepth);
        float depthWeight = 1.0 - smoothstep(depthThreshold * 0.5, depthThreshold, depthDiff);
        
        // Optional: slight normal similarity bias to maintain surface coherence
        // but still allow significant blending across normal discontinuities
        float normalSimilarity = dot(normalize(decodedNormal), normalize(centerNormal));
        float normalWeight = 0.5 + (0.5 * max(normalSimilarity, 0.0)); // Range: 0.5 to 1.0
        
        // Distance weight: Gaussian falloff from center with wider sigma
        float distWeight = exp(-0.5 * (radius * radius) / sigma2);
        
        // Combined weight with smooth transitions
        float weight = depthWeight * normalWeight * distWeight * sampleNormal.a;
        
        // Accumulate
        accumulatedNormal += decodedNormal * weight;
        totalWeight += weight;
    }
    
    // Normalize the accumulated result
    return normalize(accumulatedNormal / max(totalWeight, 0.001));
}
#endif // VGE_NORMAL_SMOOTHING_GLSL