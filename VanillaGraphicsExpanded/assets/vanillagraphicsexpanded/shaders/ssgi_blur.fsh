#version 330 core

in vec2 uv;
out vec4 outColor;

uniform sampler2D ssgiInput;
uniform sampler2D depthTexture;
uniform sampler2D normalTexture;

uniform vec2 bufferSize;
uniform int blurRadius;
uniform float depthThreshold;
uniform float normalThreshold;
uniform float zNear;
uniform float zFar;

// ============================================================================
// SSGI Spatial Blur Pass
// 
// Edge-aware blur that spreads sparse ray samples while preserving geometry edges.
// Runs at SSGI resolution (typically half-res) for better performance.
// ============================================================================

float linearizeDepth(float depth)
{
    return (2.0 * zNear * zFar) / (zFar + zNear - depth * (zFar - zNear));
}

vec3 decodeNormal(vec4 normalSample)
{
    return normalize(normalSample.xyz * 2.0 - 1.0);
}

void main()
{
    vec2 texelSize = 1.0 / bufferSize;
    
    // Sample center values
    vec4 centerSSGI = texture(ssgiInput, uv);
    float centerDepthRaw = texture(depthTexture, uv).r;
    
    // Skip sky pixels
    if (centerDepthRaw >= 1.0)
    {
        outColor = vec4(0.0);
        return;
    }
    
    float centerDepth = linearizeDepth(centerDepthRaw);
    vec3 centerNormal = decodeNormal(texture(normalTexture, uv));
    
    // Accumulate weighted samples
    vec3 colorSum = vec3(0.0);
    float weightSum = 0.0;
    
    int r = clamp(blurRadius, 1, 4);
    
    for (int y = -r; y <= r; y++)
    {
        for (int x = -r; x <= r; x++)
        {
            vec2 sampleUV = uv + vec2(float(x), float(y)) * texelSize;
            
            // Skip out-of-bounds samples
            if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0)
                continue;
            
            // Sample neighbor values
            vec4 sampleSSGI = texture(ssgiInput, sampleUV);
            float sampleDepthRaw = texture(depthTexture, sampleUV).r;
            
            // Skip sky samples
            if (sampleDepthRaw >= 1.0)
                continue;
            
            float sampleDepth = linearizeDepth(sampleDepthRaw);
            vec3 sampleNormal = decodeNormal(texture(normalTexture, sampleUV));
            
            // Depth-aware weight: reject samples across depth discontinuities
            float depthDiff = abs(centerDepth - sampleDepth);
            float depthWeight = exp(-depthDiff / (centerDepth * depthThreshold + 0.001));
            
            // Normal-aware weight: reject samples across surface orientation changes
            float normalDiff = 1.0 - max(0.0, dot(centerNormal, sampleNormal));
            float normalWeight = exp(-normalDiff / normalThreshold);
            
            // Spatial weight: Gaussian falloff from center
            float dist = length(vec2(float(x), float(y)));
            float spatialWeight = exp(-dist * dist / (float(r) * float(r) * 0.5 + 0.001));
            
            // Combined weight
            float weight = depthWeight * normalWeight * spatialWeight;
            
            colorSum += sampleSSGI.rgb * weight;
            weightSum += weight;
        }
    }
    
    // Normalize
    vec3 blurredColor = (weightSum > 0.001) ? colorSum / weightSum : centerSSGI.rgb;
    
    outColor = vec4(blurredColor, centerSSGI.a);
}
