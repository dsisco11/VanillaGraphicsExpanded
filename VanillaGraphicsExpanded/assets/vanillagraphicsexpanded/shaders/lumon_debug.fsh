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
// 12 = Probe-Atlas Meta Confidence (grayscale)
// 13 = Probe-Atlas Temporal Alpha (confidence-scaled)
// 14 = Probe-Atlas Meta Flags (RGB bit visualization)
// 15 = Probe-Atlas Filtered Radiance (probe-space)
// 16 = Probe-Atlas Filter Delta (abs(filtered - current))
// 17 = Probe-Atlas Gather Input Source (raw vs filtered)
// 18 = Composite AO (Phase 15)
// 19 = Composite Indirect Diffuse (Phase 15)
// 20 = Composite Indirect Specular (Phase 15)
// 21 = Composite Material (metallic, roughness, ao)
// 22 = Direct Diffuse (Phase 16)
// 23 = Direct Specular (Phase 16)
// 24 = Direct Emissive (Phase 16)
// 25 = Direct Total (diffuse+spec) (Phase 16)
// 31 = World-Probe Irradiance (combined)
// 32 = World-Probe Irradiance (selected level)
// 33 = World-Probe Confidence
// 34 = World-Probe ShortRangeAO Direction
// 35 = World-Probe ShortRangeAO Confidence
// 36 = World-Probe Hit Distance (normalized)
// 37 = World-Probe Meta Flags (heatmap)
// 38 = Blend Weights (screen vs world)
// 39 = Cross-Level Blend (selected L and weights)
// 41 = POM Metrics (heatmap from gBufferNormal.w)
// 42 = Raw Confidences (R=screenConf, G=worldConf, B=sumW)
// 43 = Contribution Only: world-probe (Phase 18)
// 44 = Contribution Only: screen-space (Phase 18)
// ============================================================================

// Import common utilities
@import "./includes/lumon_common.glsl"

// Import SH helpers for mode 8
@import "./includes/lumon_sh.glsl"

// Phase 18: world-probe clipmap sampling + uniforms
@import "./includes/lumon_worldprobe.glsl"

// Import probe-atlas meta helpers for mode 14
@import "./includes/lumon_probe_atlas_meta.glsl"

// Phase 14 velocity helpers
@import "./includes/velocity_common.glsl"

// Phase 15: composite math (shared with lumon_combine)
@import "./includes/lumon_pbr.glsl"

// Import global defines (feature toggles with defaults)
@import "./includes/vge_global_defines.glsl"

// Shared hash helpers
@import "./includes/squirrel3.glsl"

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

// Screen-probe atlas textures
uniform sampler2D probeAtlasMeta;       // R = confidence, G = uintBitsToFloat(flags)
uniform sampler2D probeAtlasCurrent;     // RGBA16F radiance atlas (post-temporal)
uniform sampler2D probeAtlasFiltered;    // RGBA16F radiance atlas (post-filter)
uniform sampler2D probeAtlasGatherInput; // The atlas currently selected as gather input

// Phase 15: compositing debug inputs
uniform sampler2D indirectDiffuseFull;   // Upsampled indirect buffer (full-res)
uniform sampler2D gBufferAlbedo;         // Albedo (fallback: captured scene)
uniform sampler2D gBufferMaterial;       // Material properties (roughness/metallic/emissive/reflectivity)

// Phase 16: direct lighting debug inputs
uniform sampler2D directDiffuse;
uniform sampler2D directSpecular;
uniform sampler2D emissive;

// Phase 14: velocity buffer (RGBA32F)
uniform sampler2D velocityTex;

// Phase 18: world-probe lifecycle debug atlas (RGBA16 UNorm)
uniform sampler2D worldProbeDebugState0;

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

// Phase 14: velocity debug scaling
uniform float velocityRejectThreshold;

// Matrices for reprojection
uniform mat4 invViewMatrix;
uniform mat4 prevViewProjMatrix;

// Debug mode
uniform int debugMode;

// Gather atlas selection: 0=trace, 1=current, 2=filtered
uniform int gatherAtlasSource;

// Phase 15: compositing parameters (to match lumon_combine behavior)
uniform float indirectIntensity;
uniform vec3 indirectTint;
uniform float diffuseAOStrength;
uniform float specularAOStrength;

// Some GLSL compilers are picky about calling functions before definition.
mat4 getViewMatrix();

// ============================================================================
// Debug Mode 18-21: Composite Debug Views (Phase 15)
// ============================================================================

void computeCompositeSplit(
    out float outAo,
    out float outRoughness,
    out float outMetallic,
    out vec3 outIndirectDiffuse,
    out vec3 outIndirectSpecular)
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        outAo = 1.0;
        outRoughness = 0.0;
        outMetallic = 0.0;
        outIndirectDiffuse = vec3(0.0);
        outIndirectSpecular = vec3(0.0);
        return;
    }

    vec3 indirect = texture(indirectDiffuseFull, uv).rgb;
    indirect *= indirectIntensity;
    indirect *= indirectTint;

    vec3 albedo = lumonGetAlbedo(gBufferAlbedo, uv);

    float roughness;
    float metallic;
    float emissive;
    float reflectivity;
    lumonGetMaterialProperties(gBufferMaterial, uv, roughness, metallic, emissive, reflectivity);

    // AO is not implemented yet. Keep it as a no-op (1.0).
    // NOTE: gBufferMaterial.a is reflectivity, not AO.
    float ao = 1.0;
#if VGE_LUMON_ENABLE_AO
    // NaN-guard references: does not change behavior for valid values.
    if (diffuseAOStrength != diffuseAOStrength) ao = 0.0;
    if (specularAOStrength != specularAOStrength) ao = 0.0;
#endif

    // Legacy path compatibility: if PBR composite is off, treat all indirect as diffuse.
#if !VGE_LUMON_PBR_COMPOSITE
    outAo = ao;
    outRoughness = roughness;
    outMetallic = metallic;
    outIndirectDiffuse = indirect;
    outIndirectSpecular = vec3(0.0);
    return;
#else

    vec3 viewPosVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 viewDirVS = normalize(-viewPosVS);

    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);
    vec3 normalVS = normalize((getViewMatrix() * vec4(normalWS, 0.0)).xyz);

    vec3 shortRangeAoDirVS = normalVS;
#if VGE_LUMON_ENABLE_SHORT_RANGE_AO
    float bend = clamp((1.0 - clamp(ao, 0.0, 1.0)) * 0.5, 0.0, 0.5);
    shortRangeAoDirVS = normalize(mix(normalVS, vec3(0.0, 1.0, 0.0), bend));
#endif

    vec3 diffuseContrib;
    vec3 specContrib;

    lumonComputeIndirectSplit(
        indirect,
        albedo,
        shortRangeAoDirVS,
        viewDirVS,
        roughness,
        metallic,
        ao,
        diffuseAOStrength,
        specularAOStrength,
        diffuseContrib,
        specContrib);

    outAo = ao;
    outRoughness = roughness;
    outMetallic = metallic;
    outIndirectDiffuse = diffuseContrib;
    outIndirectSpecular = specContrib;
#endif // VGE_LUMON_PBR_COMPOSITE
}

vec4 renderCompositeAoDebug()
{
    float ao;
    float roughness;
    float metallic;
    vec3 diff;
    vec3 spec;
    computeCompositeSplit(ao, roughness, metallic, diff, spec);
    return vec4(vec3(clamp(ao, 0.0, 1.0)), 1.0);
}

vec4 renderCompositeIndirectDiffuseDebug()
{
    float ao;
    float roughness;
    float metallic;
    vec3 diff;
    vec3 spec;
    computeCompositeSplit(ao, roughness, metallic, diff, spec);
    return vec4(diff, 1.0);
}

vec4 renderCompositeIndirectSpecularDebug()
{
    float ao;
    float roughness;
    float metallic;
    vec3 diff;
    vec3 spec;
    computeCompositeSplit(ao, roughness, metallic, diff, spec);
    return vec4(spec, 1.0);
}

// ============================================================================
// Debug Mode 41: POM Metrics
// ============================================================================

vec4 renderPomMetricsDebug()
{
    // Patched chunk shaders optionally write a scalar diagnostic into gBufferNormal.w.
    // Interpretation is controlled by MaterialAtlas.PomDebugMode.
    float v = clamp(texture(gBufferNormal, uv).w, 0.0, 1.0);
    vec3 c = vec3(1.0 - v, v, 0.0);
    return vec4(c, 1.0);
}

vec4 renderCompositeMaterialDebug()
{
    float ao;
    float roughness;
    float metallic;
    vec3 diff;
    vec3 spec;
    computeCompositeSplit(ao, roughness, metallic, diff, spec);
    return vec4(clamp(vec3(metallic, roughness, ao), 0.0, 1.0), 1.0);
}

// ============================================================================
// Debug Mode 29: Material Bands (Phase 7)
//
// Vintage Story's terrain path doesn't have a per-pixel MaterialIndex.
// This mode approximates a "material id" visualization by hashing the
// gBufferMaterial values (roughness/metallic/emissive/reflectivity) into
// stable color bands.
// ============================================================================

vec4 renderMaterialBandsDebug()
{
    vec4 m = clamp(texture(gBufferMaterial, uv), 0.0, 1.0);

    // Quantize to 8-bit per channel before hashing to make the visualization stable.
    uvec4 q = uvec4(m * 255.0 + 0.5);

    uint key = (q.r) | (q.g << 8) | (q.b << 16) | (q.a << 24);
    uint h = Squirrel3HashU(key);

    vec3 c = vec3(
        float((h) & 255u) / 255.0,
        float((h >> 8) & 255u) / 255.0,
        float((h >> 16) & 255u) / 255.0);

    // Snap to visible bands (reduces noisy gradients).
    c = floor(c * 6.0 + 0.5) / 6.0;

    return vec4(c, 1.0);
}

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
// Debug Mode 26-28: Velocity Debug Views (Phase 14)
// ============================================================================

vec4 renderVelocityMagnitudeDebug()
{
    vec4 velSample = texture(velocityTex, uv);
    vec2 v = lumonVelocityDecodeUv(velSample);
    uint flags = lumonVelocityDecodeFlags(velSample);

    if (!lumonVelocityIsValid(flags))
    {
        // Invalid velocity: dark red.
        return vec4(0.25, 0.0, 0.0, 1.0);
    }

    float mag = lumonVelocityMagnitude(v);
    float denom = max(velocityRejectThreshold, 1e-6);
    float t = clamp(mag / denom, 0.0, 1.0);
    return vec4(heatmap(t), 1.0);
}

vec4 renderVelocityValidityDebug()
{
    vec4 velSample = texture(velocityTex, uv);
    uint flags = lumonVelocityDecodeFlags(velSample);

    if (lumonVelocityIsValid(flags))
    {
        // Valid: green
        return vec4(0.0, 1.0, 0.0, 1.0);
    }

    // Invalid: show a reason tint when available.
    if ((flags & LUMON_VEL_FLAG_HISTORY_INVALID) != 0u) return vec4(1.0, 0.0, 1.0, 1.0); // magenta
    if ((flags & LUMON_VEL_FLAG_SKY_OR_INVALID_DEPTH) != 0u) return vec4(0.0, 0.5, 1.0, 1.0); // blue
    if ((flags & LUMON_VEL_FLAG_PREV_BEHIND_CAMERA) != 0u) return vec4(1.0, 0.0, 0.0, 1.0); // red
    if ((flags & LUMON_VEL_FLAG_PREV_OOB) != 0u) return vec4(1.0, 1.0, 0.0, 1.0); // yellow
    if ((flags & LUMON_VEL_FLAG_NAN) != 0u) return vec4(0.0, 1.0, 1.0, 1.0); // cyan

    return vec4(0.25, 0.25, 0.25, 1.0);
}

vec4 renderVelocityPrevUvDebug()
{
    vec4 velSample = texture(velocityTex, uv);
    vec2 v = lumonVelocityDecodeUv(velSample);
    uint flags = lumonVelocityDecodeFlags(velSample);

    if (!lumonVelocityIsValid(flags))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec2 prevUv = uv - v;
    return vec4(clamp(prevUv, 0.0, 1.0), 0.0, 1.0);
}

vec3 vgeTonemapReinhard(vec3 c)
{
    c = max(c, vec3(0.0));
    return c / (c + vec3(1.0));
}

vec4 renderDirectDiffuseDebug() {
    return vec4(vgeTonemapReinhard(texture(directDiffuse, uv).rgb), 1.0);
}

vec4 renderDirectSpecularDebug() {
    return vec4(vgeTonemapReinhard(texture(directSpecular, uv).rgb), 1.0);
}

vec4 renderDirectEmissiveDebug() {
    return vec4(vgeTonemapReinhard(texture(emissive, uv).rgb), 1.0);
}

vec4 renderDirectTotalDebug() {
    vec3 dd = texture(directDiffuse, uv).rgb;
    vec3 ds = texture(directSpecular, uv).rgb;
    return vec4(vgeTonemapReinhard(dd + ds), 1.0);
}

// ============================================================================
// Debug Mode 12/13: Probe-Atlas Meta
// ============================================================================

vec4 renderProbeAtlasMetaConfidenceDebug() {
    float conf = texture(probeAtlasMeta, uv).r;
    return vec4(vec3(clamp(conf, 0.0, 1.0)), 1.0);
}

vec4 renderProbeAtlasTemporalAlphaDebug() {
    float confHist = texture(probeAtlasMeta, uv).r;
    float alphaEff = clamp(temporalAlpha * clamp(confHist, 0.0, 1.0), 0.0, 1.0);
    return vec4(vec3(alphaEff), 1.0);
}

vec4 renderProbeAtlasMetaFlagsDebug() {
    float conf;
    uint flags;
    lumonDecodeMeta(texture(probeAtlasMeta, uv).rg, conf, flags);

    float hit = (flags & LUMON_META_HIT) != 0u ? 1.0 : 0.0;
    float sky = (flags & LUMON_META_SKY_MISS) != 0u ? 1.0 : 0.0;
    float exit = (flags & LUMON_META_SCREEN_EXIT) != 0u ? 1.0 : 0.0;

    // Encode additional bits as brightness boost so they pop without hiding base flags.
    float early = (flags & LUMON_META_EARLY_TERMINATED) != 0u ? 0.25 : 0.0;
    float thick = (flags & LUMON_META_THICKNESS_UNCERT) != 0u ? 0.25 : 0.0;

    vec3 base = vec3(hit, sky, exit);
    base = clamp(base + vec3(early + thick), 0.0, 1.0);
    return vec4(base, 1.0);
}

vec4 renderProbeAtlasFilteredRadianceDebug() {
    vec3 rgb = texture(probeAtlasFiltered, uv).rgb;
    return vec4(rgb, 1.0);
}

vec4 renderProbeAtlasFilterDeltaDebug() {
    vec3 curr = texture(probeAtlasCurrent, uv).rgb;
    vec3 filt = texture(probeAtlasFiltered, uv).rgb;
    float d = length(filt - curr);
    // Scale a bit so subtle changes show up.
    return vec4(heatmap(clamp(d * 4.0, 0.0, 1.0)), 1.0);
}

vec4 renderProbeAtlasGatherInputSourceDebug() {
    // Solid color to make it obvious what the renderer is feeding into gather.
    // Red = trace/raw, Yellow = temporal/current, Green = filtered
    if (gatherAtlasSource == 2) return vec4(0.1, 1.0, 0.1, 1.0);
    if (gatherAtlasSource == 1) return vec4(1.0, 1.0, 0.1, 1.0);
    return vec4(1.0, 0.1, 0.1, 1.0);
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
// Debug Modes 43-44: Contribution Split (Phase 18)
// Shows only the world-probe or only the screen-space portion of the final
// screen-first blend written into indirectHalf.
// ============================================================================

vec3 lumonTonemapReinhard(vec3 hdr)
{
    hdr = max(hdr, vec3(0.0));
    return hdr / (hdr + vec3(1.0));
}

vec3 lumonComputeWorldProbeContributionOnly()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec3(0.0);
    }

    float sumW = clamp(texture(indirectHalf, uv).a, 0.0, 1.0);
    if (sumW <= 1e-6)
    {
        return vec3(0.0);
    }

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return vec3(0.0);
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    float worldConf = clamp(wp.confidence, 0.0, 1.0);

    // Reconstruct the screen-first blend weights using the stored final confidence (sumW)
    // and the raw world confidence (worldConf). This matches the derivation used by
    // renderWorldProbeBlendWeightsDebug().
    float screenW = (worldConf >= 0.999)
        ? 0.0
        : clamp((sumW - worldConf) / max(1.0 - worldConf, 1e-6), 0.0, 1.0);
    float worldW = worldConf * (1.0 - screenW);

    vec3 worldContrib = wp.irradiance * (worldW / max(sumW, 1e-6));

    // Match the gather output space (gather pass applies these before writing indirectHalf).
    worldContrib *= indirectIntensity;
    worldContrib *= indirectTint;

    return max(worldContrib, vec3(0.0));
#endif
}

vec4 renderWorldProbeContributionOnlyDebug()
{
    vec3 worldContrib = lumonComputeWorldProbeContributionOnly();
    return vec4(lumonTonemapReinhard(worldContrib), 1.0);
}

vec4 renderScreenSpaceContributionOnlyDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // indirectHalf.rgb contains the *blended* (screen+world) irradiance in gather output space.
    vec3 blended = texture(indirectHalf, uv).rgb;

    // Derive screen portion as blended - worldContribution.
    vec3 worldContrib = lumonComputeWorldProbeContributionOnly();
    vec3 screenContrib = max(blended - worldContrib, vec3(0.0));

    return vec4(lumonTonemapReinhard(screenContrib), 1.0);
}

// ============================================================================
// Debug Modes 31-39, 41: World-Probe Clipmap (Phase 18)
// ============================================================================

vec4 lumonWorldProbeDebugDisabledColor()
{
    // Visual cue that the world-probe debug path is compile-time disabled (vs just "no data in bounds").
    float v = 0.5 + 0.5 * sin(uv.x * 80.0) * sin(uv.y * 80.0);
    vec3 a = vec3(0.15, 0.0, 0.2);
    vec3 b = vec3(0.55, 0.0, 0.7);
    return vec4(mix(a, b, v), 1.0);
}

bool lumonWorldProbeDebugNearest(in vec3 worldPosWS, out int outLevel, out ivec2 outAtlasCoord)
{
#if !VGE_LUMON_WORLDPROBE_ENABLED
    outLevel = 0;
    outAtlasCoord = ivec2(0);
    return false;
#else
    int levels = VGE_LUMON_WORLDPROBE_LEVELS;
    int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;
    float baseSpacing = VGE_LUMON_WORLDPROBE_BASE_SPACING;

    if (levels <= 0 || resolution <= 0)
    {
        outLevel = 0;
        outAtlasCoord = ivec2(0);
        return false;
    }

    int maxLevel = max(levels - 1, 0);
    int level = lumonWorldProbeSelectLevelByDistance(worldPosWS, worldProbeCameraPosWS, baseSpacing, maxLevel);
    float spacing = lumonWorldProbeSpacing(baseSpacing, level);

    vec3 origin = worldProbeOriginMinCorner[level];
    vec3 worldPosRel = worldPosWS - worldProbeCameraPosWS;
    vec3 local = (worldPosRel - origin) / max(spacing, 1e-6);

    // Outside clip volume.
    if (any(lessThan(local, vec3(0.0))) || any(greaterThanEqual(local, vec3(float(resolution)))))
    {
        outLevel = level;
        outAtlasCoord = ivec2(0);
        return false;
    }

    ivec3 idx = ivec3(floor(local + 0.5));
    idx = clamp(idx, ivec3(0), ivec3(resolution - 1));

    ivec3 ring = ivec3(floor(worldProbeRingOffset[level] + 0.5));
    ivec3 storage = lumonWorldProbeWrapIndex(idx + ring, resolution);

    outLevel = level;
    outAtlasCoord = lumonWorldProbeAtlasCoord(storage, level, resolution);
    return true;
#endif
}

vec3 lumonWorldProbeDebugToneMap(vec3 hdr)
{
    hdr = max(hdr, vec3(0.0));
    return hdr / (hdr + vec3(1.0));
}

vec4 renderWorldProbeIrradianceCombinedDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    vec3 color = lumonWorldProbeDebugToneMap(wp.irradiance);
    return vec4(color, 1.0);
#endif
}

vec4 renderWorldProbeIrradianceLevelDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    int levels = VGE_LUMON_WORLDPROBE_LEVELS;
    int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;
    float baseSpacing = VGE_LUMON_WORLDPROBE_BASE_SPACING;
    if (levels <= 0 || resolution <= 0) return vec4(0.0, 0.0, 0.0, 1.0);

    int maxLevel = max(levels - 1, 0);
    int level = lumonWorldProbeSelectLevelByDistance(posWS, worldProbeCameraPosWS, baseSpacing, maxLevel);
    float spacing = lumonWorldProbeSpacing(baseSpacing, level);
    vec3 posRel = posWS - worldProbeCameraPosWS;

    LumOnWorldProbeSample sL = lumonWorldProbeSampleLevelTrilinear(
        worldProbeSH0, worldProbeSH1, worldProbeSH2, worldProbeSky0, worldProbeVis0, worldProbeMeta0,
        posRel, normalWS,
        worldProbeOriginMinCorner[level], worldProbeRingOffset[level],
        spacing, resolution, level);

    vec3 color = lumonWorldProbeDebugToneMap(sL.irradiance);
    // Encode selected level in alpha for quick inspection.
    float a = (levels > 1) ? float(level) / float(max(levels - 1, 1)) : 0.0;
    return vec4(color, a);
#endif
}

vec4 renderWorldProbeConfidenceDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    float c = clamp(wp.confidence, 0.0, 1.0);
    return vec4(vec3(c), 1.0);
#endif
}

vec4 renderWorldProbeAoDirectionDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#endif

    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;

    int level;
    ivec2 ac;
    if (!lumonWorldProbeDebugNearest(posWS, level, ac))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec4 vis = texelFetch(worldProbeVis0, ac, 0);
    vec3 aoDir = lumonOctahedralUVToDirection(vis.xy);
    float aoConf = clamp(vis.w, 0.0, 1.0);

    vec3 color = aoDir * 0.5 + 0.5;
    color *= aoConf;
    return vec4(color, 1.0);
}

vec4 renderWorldProbeAoConfidenceDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#endif

    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;

    int level;
    ivec2 ac;
    if (!lumonWorldProbeDebugNearest(posWS, level, ac))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    float aoConf = clamp(texelFetch(worldProbeVis0, ac, 0).w, 0.0, 1.0);
    return vec4(vec3(aoConf), 1.0);
}

vec4 renderWorldProbeDistanceDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#endif

    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;

    int level;
    ivec2 ac;
    if (!lumonWorldProbeDebugNearest(posWS, level, ac))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    float baseSpacing = VGE_LUMON_WORLDPROBE_BASE_SPACING;
    int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;
    float spacing = lumonWorldProbeSpacing(baseSpacing, level);

    float meanLog = texelFetch(worldProbeDist0, ac, 0).x; // log(dist+1)
    float dist = exp(meanLog) - 1.0;

    float maxDist = max(spacing * float(resolution), 1e-3);
    float t = clamp(dist / maxDist, 0.0, 1.0);
    t = sqrt(t);
    return vec4(vec3(t), 1.0);
}

vec4 renderWorldProbeFlagsHeatmapDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#endif

    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;

    int level;
    ivec2 ac;
    if (!lumonWorldProbeDebugNearest(posWS, level, ac))
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // R=stale/dirty, G=in-flight, B=valid.
    vec3 color = texelFetch(worldProbeDebugState0, ac, 0).rgb;
    return vec4(color, 1.0);
}

vec4 renderWorldProbeBlendWeightsDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    // indirectHalf alpha encodes the final (screen+world) confidence.
    float sumW = clamp(texture(indirectHalf, uv).a, 0.0, 1.0);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    float worldConf = clamp(wp.confidence, 0.0, 1.0);

    float screenW = (worldConf >= 0.999)
        ? 0.0
        : clamp((sumW - worldConf) / max(1.0 - worldConf, 1e-6), 0.0, 1.0);

    float worldW = worldConf * (1.0 - screenW);

    // R=screen weight, G=world weight.
    return vec4(screenW, worldW, 0.0, 1.0);
#endif
}

vec4 renderWorldProbeRawConfidencesDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;
    vec3 normalWS = lumonDecodeNormal(texture(gBufferNormal, uv).xyz);

    // Final confidence from gather (screen-first blend result).
    float sumW = clamp(texture(indirectHalf, uv).a, 0.0, 1.0);

    LumOnWorldProbeSample wp = lumonWorldProbeSampleClipmapBound(posWS, normalWS);
    float worldConf = clamp(wp.confidence, 0.0, 1.0);

    // Reconstruct the screen confidence (screenW) from sumW and worldConf.
    // This matches the gather blend:
    //   sumW = screenW + worldConf * (1 - screenW)
    float screenConf = (worldConf >= 0.999)
        ? 0.0
        : clamp((sumW - worldConf) / max(1.0 - worldConf, 1e-6), 0.0, 1.0);

    return vec4(screenConf, worldConf, sumW, 1.0);
#endif
}

vec4 renderWorldProbeCrossLevelBlendDebug()
{
    float depth = texture(primaryDepth, uv).r;
    if (lumonIsSky(depth)) return vec4(0.0, 0.0, 0.0, 1.0);

#if !VGE_LUMON_WORLDPROBE_ENABLED
    return lumonWorldProbeDebugDisabledColor();
#else
    vec3 posVS = lumonReconstructViewPos(uv, depth, invProjectionMatrix);
    vec3 posWS = (invViewMatrix * vec4(posVS, 1.0)).xyz;

    int levels = VGE_LUMON_WORLDPROBE_LEVELS;
    int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;
    float baseSpacing = VGE_LUMON_WORLDPROBE_BASE_SPACING;
    if (levels <= 0 || resolution <= 0) return vec4(0.0, 0.0, 0.0, 1.0);

    int maxLevel = max(levels - 1, 0);
    int level = lumonWorldProbeSelectLevelByDistance(posWS, worldProbeCameraPosWS, baseSpacing, maxLevel);
    float spacingL = lumonWorldProbeSpacing(baseSpacing, level);

    vec3 originL = worldProbeOriginMinCorner[level];
    vec3 posRel = posWS - worldProbeCameraPosWS;
    vec3 localL = (posRel - originL) / max(spacingL, 1e-6);
    float edgeDist = lumonWorldProbeDistanceToBoundaryProbeUnits(localL, resolution);
    float wL = lumonWorldProbeCrossLevelBlendWeight(edgeDist, 2.0, 2.0);

    float levelN = (levels > 1) ? float(level) / float(max(levels - 1, 1)) : 0.0;
    // R = selected level (normalized), G = weight for L, B = weight for L+1.
    return vec4(levelN, wL, 1.0 - wL, 1.0);
#endif
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
        case 42:
            outColor = renderWorldProbeRawConfidencesDebug();
            break;
        case 43:
            outColor = renderWorldProbeContributionOnlyDebug();
            break;
        case 44:
            outColor = renderScreenSpaceContributionOnlyDebug();
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
        case 12:
            outColor = renderProbeAtlasMetaConfidenceDebug();
            break;
        case 13:
            outColor = renderProbeAtlasTemporalAlphaDebug();
            break;
        case 14:
            outColor = renderProbeAtlasMetaFlagsDebug();
            break;
        case 15:
            outColor = renderProbeAtlasFilteredRadianceDebug();
            break;
        case 16:
            outColor = renderProbeAtlasFilterDeltaDebug();
            break;
        case 17:
            outColor = renderProbeAtlasGatherInputSourceDebug();
            break;
        case 18:
            outColor = renderCompositeAoDebug();
            break;
        case 19:
            outColor = renderCompositeIndirectDiffuseDebug();
            break;
        case 20:
            outColor = renderCompositeIndirectSpecularDebug();
            break;
        case 21:
            outColor = renderCompositeMaterialDebug();
            break;
        case 22:
            outColor = renderDirectDiffuseDebug();
            break;
        case 23:
            outColor = renderDirectSpecularDebug();
            break;
        case 24:
            outColor = renderDirectEmissiveDebug();
            break;
        case 25:
            outColor = renderDirectTotalDebug();
            break;
        case 26:
            outColor = renderVelocityMagnitudeDebug();
            break;
        case 27:
            outColor = renderVelocityValidityDebug();
            break;
        case 28:
            outColor = renderVelocityPrevUvDebug();
            break;
        case 29:
            outColor = renderMaterialBandsDebug();
            break;
        case 31:
            outColor = renderWorldProbeIrradianceCombinedDebug();
            break;
        case 32:
            outColor = renderWorldProbeIrradianceLevelDebug();
            break;
        case 33:
            outColor = renderWorldProbeConfidenceDebug();
            break;
        case 34:
            outColor = renderWorldProbeAoDirectionDebug();
            break;
        case 35:
            outColor = renderWorldProbeAoConfidenceDebug();
            break;
        case 36:
            outColor = renderWorldProbeDistanceDebug();
            break;
        case 37:
            outColor = renderWorldProbeFlagsHeatmapDebug();
            break;
        case 38:
            outColor = renderWorldProbeBlendWeightsDebug();
            break;
        case 39:
            outColor = renderWorldProbeCrossLevelBlendDebug();
            break;
        case 41:
            outColor = renderPomMetricsDebug();
            break;
        default:
            outColor = vec4(1.0, 0.0, 1.0, 1.0);  // Magenta = unknown mode
            break;
    }
}
