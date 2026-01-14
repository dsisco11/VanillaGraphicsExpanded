// PBR shadow map sampling helpers
//
// Uses Vintage Story's near/far shadow coordinate weighting logic (ported from
// assets/game/shaderincludes/shadowcoords.vsh) and PCF sampling style (ported from
// assets/game/shaderincludes/fogandlight.fsh).
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

void pbrCalcShadowMapCoords(vec3 worldPosRel, out vec4 shadowCoordsNear, out vec4 shadowCoordsFar)
{
    shadowCoordsNear = vec4(0.0);
    shadowCoordsFar = vec4(0.0);

    // Vanilla uses length(vec4(worldPos, 1)), but vec3 is close enough and avoids w contamination.
    float len = length(worldPosRel);
    float nearSub = 0.0;

    // Near map
    if (shadowRangeNear > 0.0)
    {
        shadowCoordsNear = toShadowMapSpaceMatrixNear * vec4(worldPosRel, 1.0);

        float distanceNear = clamp(
            max(max(0.0, 0.03 - shadowCoordsNear.x) * 100.0, max(0.0, shadowCoordsNear.x - 0.97) * 100.0) +
            max(max(0.0, 0.03 - shadowCoordsNear.y) * 100.0, max(0.0, shadowCoordsNear.y - 0.97) * 100.0) +
            max(0.0, shadowCoordsNear.z - 0.98) * 100.0 +
            max(0.0, len / shadowRangeNear - 0.15)
        , 0.0, 1.0);

        nearSub = shadowCoordsNear.w = clamp(1.0 - distanceNear, 0.0, 1.0);
        if (shadowCoordsNear.z >= 0.999) shadowCoordsNear.w = 0.0;
    }

    // Far map
    if (shadowRangeFar > 0.0)
    {
        shadowCoordsFar = toShadowMapSpaceMatrixFar * vec4(worldPosRel, 1.0);

        float distanceFar = clamp(
            max(max(0.0, 0.03 - shadowCoordsFar.x) * 10.0, max(0.0, shadowCoordsFar.x - 0.97) * 10.0) +
            max(max(0.0, 0.03 - shadowCoordsFar.y) * 10.0, max(0.0, shadowCoordsFar.y - 0.97) * 10.0) +
            max(0.0, shadowCoordsFar.z - 0.98) * 10.0 +
            max(0.0, len / shadowRangeFar - 0.15)
        , 0.0, 1.0);

        distanceFar = distanceFar * 2.0 - 0.5;
        shadowCoordsFar.w = max(0.0, clamp(1.0 - distanceFar, 0.0, 1.0) - nearSub);
        if (shadowCoordsFar.z >= 0.999) shadowCoordsFar.w = 0.0;
    }
}

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

float pbrComputeSunShadowVisibility(vec3 worldPosRel)
{
    // When intensity is 0, avoid sampling shadow maps at all.
    if (dropShadowIntensity <= 0.0001) return 1.0;
    if (shadowRangeNear <= 0.0 && shadowRangeFar <= 0.0) return 1.0;

    vec4 scNear;
    vec4 scFar;
    pbrCalcShadowMapCoords(worldPosRel, scNear, scFar);

    float occlusion = 0.0;

    // Bias values taken from vanilla fogandlight.fsh.
    if (scFar.w > 0.0)
    {
        occlusion += pbrShadowOcclusionPcf3x3(shadowMapFar, scFar, 0.0009) * scFar.w * 0.5;
    }

    if (scNear.w > 0.0)
    {
        occlusion += pbrShadowOcclusionPcf3x3(shadowMapNear, scNear, 0.0005) * scNear.w * 0.5;
    }

    float visibility = 1.0 - dropShadowIntensity * occlusion;
    return clamp(visibility, 0.0, 1.0);
}

#endif
