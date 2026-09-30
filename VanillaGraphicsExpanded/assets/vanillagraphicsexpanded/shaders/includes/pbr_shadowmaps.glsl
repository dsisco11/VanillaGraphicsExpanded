// PBR shadow map sampling helpers
//
// Uses Vintage Story's near/far shadow coordinate weighting logic (ported from
// assets/game/shaderincludes/shadowcoords.vsh) and PCF sampling style (ported from
// assets/game/shaderincludes/fogandlight.fsh). Cascade occlusion controls only direct
// sunlight here; vanilla's half-strength darkening of combined lighting does not apply.
//
// IMPORTANT: All positions passed to these helpers must be in the same space as
// the engine's shadow matrices expect: camera-relative world space ("worldPos" in
// the vanilla chunk shaders).

#ifndef VGE_PBR_SHADOWMAPS_GLSL
#define VGE_PBR_SHADOWMAPS_GLSL

// Expected uniforms (declared by the including shader):
// uniform sampler2DShadow shadowMapNear;
// uniform sampler2DShadow shadowMapFar;
// uniform mat4 toShadowMapSpaceMatrixNear;
// uniform mat4 toShadowMapSpaceMatrixFar;
// uniform float shadowRangeNear;
// uniform float shadowRangeFar;
// uniform float dropShadowIntensity;

@import "./pbr_shadowcoords.glsl"

/// Measures the occluded fraction of a 3x3 comparison-sampled shadow footprint.
float pbrShadowOcclusionPcf3x3(sampler2DShadow shadowMap, vec4 shadowCoords, float bias)
{
    // Returns occlusion in [0,1] (0=fully lit, 1=fully shadowed)
    ivec2 size = textureSize(shadowMap, 0);
    vec2 invSize = 1.0 / vec2(max(size, ivec2(1)));

    float sum = 0.0;
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            vec2 uvOff = vec2(float(x), float(y)) * invSize;
            sum += texture(shadowMap, vec3(shadowCoords.xy + uvOff, shadowCoords.z - bias));
        }
    }

    float lit = sum / 9.0;
    return 1.0 - lit;
}

/// Returns intensity-scaled and raw PCF direct-sun visibility.
void pbrComputeSunShadowVisibility(vec3 worldPosRel, out float visibility, out float pcfVisibility)
{
    // When intensity is 0, avoid sampling shadow maps at all.
    if (dropShadowIntensity <= 0.0001 || (shadowRangeNear <= 0.0 && shadowRangeFar <= 0.0))
    {
        visibility = 1.0;
        pcfVisibility = 1.0;
        return;
    }

    vec4 scNear;
    vec4 scFar;
    pbrCalcShadowMapCoords(worldPosRel, scNear, scFar);

    float occlusion = 0.0;

    // Bias values taken from vanilla fogandlight.fsh.
    if (scFar.w > 0.0)
    {
        occlusion += pbrShadowOcclusionPcf3x3(shadowMapFar, scFar, 0.0009) * scFar.w;
    }

    if (scNear.w > 0.0)
    {
        occlusion += pbrShadowOcclusionPcf3x3(shadowMapNear, scNear, 0.0005) * scNear.w;
    }

    pcfVisibility = clamp(1.0 - occlusion, 0.0, 1.0);
    visibility = clamp(1.0 - dropShadowIntensity * occlusion, 0.0, 1.0);
}

#endif
