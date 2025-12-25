#version 330 core

in vec2 uv;
out vec4 outColor;

// Textures
uniform sampler2D primaryScene;
uniform sampler2D primaryDepth;
uniform sampler2D gBufferNormal; // G-buffer normal from MRT

// Matrices for world position reconstruction
uniform mat4 invProjectionMatrix;
uniform mat4 invModelViewMatrix;

// Z-planes
uniform float zNear;
uniform float zFar;

// Frame info
uniform vec2 frameSize;

// Camera and lighting
uniform vec3 cameraOriginFloor; // Floor-aligned camera world position (mod 4096)
uniform vec3 cameraOriginFrac;  // Fractional part of camera position
uniform vec3 lightDirection;
uniform vec3 rgbaAmbientIn;
uniform vec3 rgbaLightIn;

// Debug mode: 0=PBR, 1=normals (blurred), 2=roughness, 3=metallic, 4=worldPos, 5=depth, 6=normals (raw)
uniform int debugMode;

// Normal blur settings (Teardown-style)
uniform int normalQuality;      // Sample count: 0=off, 4, 8, 12, 16 (higher = smoother but slower)
uniform float normalBlurRadius; // Blur radius in pixels (typically 1.0-3.0)

// Distance falloff settings for procedural PBR values
uniform float pbrFalloffStart;  // Distance where falloff begins (in blocks)
uniform float pbrFalloffEnd;    // Distance where procedural values fully fade out

// PBR constants
const float PATCH_SIZE = 1f / 32f; // 1/32nd of a block
const float ROUGHNESS_MIN = 0.3;
const float ROUGHNESS_MAX = 0.4;
const float ROUGHNESS_DEFAULT = 0.8;  // Default roughness at far distance
const float METALLIC_BASE = 0.0;
const float PI = 3.14159265359;

// Hash function for procedural values
float hash(vec3 p) {
    p = fract(p * vec3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return fract((p.x + p.y) * p.z);
}

// Linearize depth from depth buffer
float linearizeDepth(float depth) {
    float z = depth * 2.0 - 1.0; // Back to NDC
    return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
}

// Golden ratio constant for spiral sampling (Teardown-style)
const float PHI = 1.618033988749895;
const float TAU = 6.283185307179586; // 2 * PI
const float GOLDEN_ANGLE = 2.399963229728653; // TAU / PHI^2 ≈ 137.5 degrees in radians

// Teardown-style normal blurring using golden ratio spiral sampling
// This gives the illusion of beveled edges after shading
// Reference: https://juandiegomontoya.github.io/teardown_breakdown.html
//
// KEY INSIGHT: Unlike typical bilateral blur that preserves edges,
// Teardown WANTS to blur across normal discontinuities to create fake bevels.
// We only reject samples across DEPTH discontinuities (different objects).
vec3 sampleNormalSmooth(sampler2D normalTex, sampler2D depthTex, vec2 texCoord, vec2 texelSize, float centerDepth, int sampleCount, float blurRadius) {
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
    float depthThreshold = max(0.05, centerLinearDepth * 0.05); // ~5% of depth, min 0.15 blocks
    // float depthThreshold = (centerLinearDepth * 0.03); // ~3% of depth, min 0.15 blocks
    
    // Accumulate weighted normals - start with lower center weight for more averaging
    float totalWeight = 0.1;
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
        // float normalSimilarity = dot(normalize(decodedNormal), normalize(centerNormal));
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

// Reconstruct view-space position from depth
vec3 reconstructViewPos(vec2 texCoord, float depth) {
    // Convert to NDC
    vec4 ndc = vec4(texCoord * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    
    // Transform by inverse projection
    vec4 viewPos = invProjectionMatrix * ndc;
    viewPos /= viewPos.w;
    
    return viewPos.xyz;
}

// Reconstruct world position from view position
vec3 reconstructWorldPos(vec3 viewPos) {
    vec4 relPos = invModelViewMatrix * vec4(viewPos, 1.0);
    // relPos.xyz is relative to camera at origin
    // Add camera world position (split into floor + frac for precision)
    // Small offset (0.001) avoids z-fighting on surfaces aligned with coordinate boundaries
    return relPos.xyz + cameraOriginFrac + cameraOriginFloor + vec3(0.001);
}

// Fresnel-Schlick approximation
vec3 fresnelSchlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// GGX/Trowbridge-Reitz normal distribution
float distributionGGX(vec3 N, vec3 H, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    
    float num = a2;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;
    
    return num / denom;
}

// Schlick-GGX geometry function
float geometrySchlickGGX(float NdotV, float roughness) {
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;
    
    float num = NdotV;
    float denom = NdotV * (1.0 - k) + k;
    
    return num / denom;
}

// Smith geometry function
float geometrySmith(vec3 N, vec3 V, vec3 L, float roughness) {
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = geometrySchlickGGX(NdotV, roughness);
    float ggx1 = geometrySchlickGGX(NdotL, roughness);
    
    return ggx1 * ggx2;
}

void main() {
    // Sample scene color and depth
    vec4 sceneColor = texture(primaryScene, uv);
    float depth = texture(primaryDepth, uv).r;
    
    // Early out for sky (depth at far plane)
    if (depth >= 0.9999) {
        outColor = sceneColor;
        return;
    }
    
    // Reconstruct positions
    vec3 viewPos = reconstructViewPos(uv, depth);
    vec3 worldPos = reconstructWorldPos(viewPos);
    
    // Get normal from G-buffer
    vec4 gNormal = texture(gBufferNormal, uv);
        
    // Calculate texel size for neighbor sampling
    vec2 texelSize = 1.0 / frameSize;
    
    // Get smoothed normal using Teardown-style golden ratio spiral blur
    vec3 worldNormal = sampleNormalSmooth(gBufferNormal, primaryDepth, uv, texelSize, depth, normalQuality, normalBlurRadius);
    
    // Calculate distance falloff factor
    float linearDepth = linearizeDepth(depth);
    // Smooth falloff using smoothstep: 1.0 at pbrFalloffStart, 0.0 at pbrFalloffEnd
    float falloffFactor = 1.0 - smoothstep(pbrFalloffStart, pbrFalloffEnd, linearDepth);
    
    // Generate procedural roughness/metallic from world position hash
    vec3 patchCoord = floor(worldPos / PATCH_SIZE);
    float hashValue = hash(patchCoord);
    float proceduralRoughness = mix(ROUGHNESS_MIN, ROUGHNESS_MAX, hashValue);
    
    // Second hash with offset for metallic variation (for debug visualization)
    // float hashValue2 = hash(patchCoord + vec3(17.0, 31.0, 47.0));
    // float proceduralMetallic = hashValue2 > 0.85 ? hashValue2 : METALLIC_BASE; // ~15% chance of metallic patches
    
    // Apply distance falloff - fade procedural values to defaults at far distances
    float roughness = mix(ROUGHNESS_DEFAULT, proceduralRoughness, falloffFactor);
    float metallic = 0;//mix(METALLIC_BASE, proceduralMetallic, falloffFactor);
    
    // Debug visualizations (used by debug overlay renderer)
    if (debugMode == 1) {
        // Visualize normals (with blur applied if enabled)
        outColor = vec4(worldNormal * 0.5 + 0.5, 1.0);
        return;
    } else if (debugMode == 2) {
        // Visualize roughness
        outColor = vec4(vec3(roughness), 1.0);
        return;
    } else if (debugMode == 3) {
        // Visualize metallic
        outColor = vec4(vec3(metallic), 1.0);
        return;
    } else if (debugMode == 4) {
        // Visualize world position (wrapped)
        outColor = vec4(fract(worldPos), 1.0);
        return;
    } else if (debugMode == 5) {
        // Visualize depth (normalized to visible range)
        float linDepth = linearizeDepth(depth);
        // Use logarithmic scale for better visualization of nearby geometry
        float normalizedDepth = log(1.0 + linDepth) / log(1.0 + zFar);
        outColor = vec4(vec3(normalizedDepth), 1.0);
        return;
    } else if (debugMode == 6) {
        // Visualize raw (unblurred) normals from G-buffer
        vec3 rawNormal = normalize(gNormal.rgb * 2.0 - 1.0);
        outColor = vec4(rawNormal * 0.5 + 0.5, 1.0);
        return;
    } else if (debugMode == 7) {
        // Debug: show blur parameters and sample count
        // Red = normalQuality/16, Green = normalBlurRadius/5, Blue = gNormal.a (should be 1.0 if valid)
        float qNorm = float(normalQuality) / 16.0;
        float rNorm = normalBlurRadius / 5.0;
        outColor = vec4(qNorm, rNorm, gNormal.a, 1.0);
        return;
    }
    
    // PBR lighting calculation
    vec3 albedo = sceneColor.rgb;
    vec3 N = worldNormal;
    // View direction: in view space, camera is at origin, so view dir is -viewPos
    // Transform to world space using inverse view matrix (direction, so w=0)
    vec3 viewDirViewSpace = normalize(-viewPos);
    vec3 V = normalize((invModelViewMatrix * vec4(viewDirViewSpace, 0.0)).xyz);
    // lightDirection is already a normalized direction vector pointing toward the sun
    vec3 L = lightDirection;
    vec3 H = normalize(V + L);
    
    // Calculate F0 (surface reflection at zero incidence)
    vec3 F0 = vec3(0.04);
    F0 = mix(F0, albedo, metallic);
    
    // Cook-Torrance BRDF
    float NDF = distributionGGX(N, H, roughness);
    float G = geometrySmith(N, V, L, roughness);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);
    
    vec3 kS = F;
    vec3 kD = vec3(1.0) - kS;
    kD *= 1.0 - metallic;
    
    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.0001;
    vec3 specular = numerator / denominator;
    
    // Combine diffuse and specular
    float NdotL = max(dot(N, L), 0.0);
    
    // Simple sun light color (warm white)
    vec3 lightColor = rgbaLightIn;// * 2.0;// vec3(1.0, 0.95, 0.9) * 2.0;
    
    vec3 Lo = (kD * albedo / PI + specular) * lightColor * NdotL;
    // vec3 Lo = (kD / (PI + specular)) * lightColor * NdotL;
    
    // Ambient lighting (simple approximation)
    vec3 ambient = rgbaAmbientIn * albedo;
    
    // Final color - blend with original based on PBR contribution
    vec3 pbrColor = ambient + Lo;
    // vec3 pbrColor = mix(ambient, Lo, 0.5);
    
    // Blend PBR result with original scene (to preserve original lighting somewhat)
    // vec3 finalColor = mix(sceneColor.rgb, pbrColor, 0.5);
    
    outColor = vec4(pbrColor, sceneColor.a);
    // outColor = vec4(finalColor, sceneColor.a);
}
