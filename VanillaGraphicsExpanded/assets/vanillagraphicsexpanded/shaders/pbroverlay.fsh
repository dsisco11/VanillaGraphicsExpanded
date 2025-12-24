#version 330 core

in vec2 uv;
out vec4 outColor;

// Textures
uniform sampler2D primaryScene;
uniform sampler2D primaryDepth;

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
uniform vec3 sunDirection;

// Debug mode: 0=PBR, 1=normals, 2=roughness, 3=metallic, 4=worldPos, 5=depth
uniform int debugMode;

// PBR constants
const float PATCH_SIZE = 0.0625; // 1/16th block
const float ROUGHNESS_MIN = 0.3;
const float ROUGHNESS_MAX = 0.9;
const float METALLIC_BASE = 0.0;
const float NORMAL_STRENGTH = 0.5;
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
    return relPos.xyz + cameraOriginFrac + cameraOriginFloor;
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
    
    // Compute screen-space normals from depth derivatives
    vec3 viewPosRight = reconstructViewPos(uv + vec2(1.0 / frameSize.x, 0.0), 
                                            texture(primaryDepth, uv + vec2(1.0 / frameSize.x, 0.0)).r);
    vec3 viewPosUp = reconstructViewPos(uv + vec2(0.0, 1.0 / frameSize.y), 
                                         texture(primaryDepth, uv + vec2(0.0, 1.0 / frameSize.y)).r);
    
    vec3 dPdx = viewPosRight - viewPos;
    vec3 dPdy = viewPosUp - viewPos;
    vec3 viewNormal = normalize(cross(dPdy, dPdx));
    
    // Transform normal to world space
    vec3 worldNormal = normalize((invModelViewMatrix * vec4(viewNormal, 0.0)).xyz);
    worldNormal = mix(vec3(0.0, 1.0, 0.0), worldNormal, NORMAL_STRENGTH);
    worldNormal = normalize(worldNormal);
    
    // Generate procedural roughness/metallic from world position hash
    vec3 patchCoord = floor(worldPos / PATCH_SIZE);
    float hashValue = hash(patchCoord);
    float roughness = mix(ROUGHNESS_MIN, ROUGHNESS_MAX, hashValue);
    
    // Second hash with offset for metallic variation (for debug visualization)
    float hashValue2 = hash(patchCoord + vec3(17.0, 31.0, 47.0));
    float metallic = hashValue2 > 0.85 ? hashValue2 : METALLIC_BASE; // ~15% chance of metallic patches
    
    // Debug visualizations
    if (debugMode == 1) {
        // Visualize normals
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
    }
    
    // PBR lighting calculation
    vec3 albedo = sceneColor.rgb;
    vec3 N = worldNormal;
    // View direction: camera is at origin in view space, so view dir is just -viewPos normalized
    // Or equivalently, camera world pos - world pos, where camera is at cameraOriginFloor + cameraOriginFrac
    vec3 cameraWorldPos = cameraOriginFloor + cameraOriginFrac;
    vec3 V = normalize(cameraWorldPos - worldPos);
    vec3 L = normalize(sunDirection);
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
    vec3 lightColor = vec3(1.0, 0.95, 0.9) * 2.0;
    
    vec3 Lo = (kD * albedo / PI + specular) * lightColor * NdotL;
    
    // Ambient lighting (simple approximation)
    vec3 ambient = vec3(0.03) * albedo;
    
    // Final color - blend with original based on PBR contribution
    vec3 pbrColor = ambient + Lo;
    
    // Blend PBR result with original scene (to preserve original lighting somewhat)
    vec3 finalColor = mix(sceneColor.rgb, pbrColor, 0.5);
    
    outColor = vec4(finalColor, sceneColor.a);
}
