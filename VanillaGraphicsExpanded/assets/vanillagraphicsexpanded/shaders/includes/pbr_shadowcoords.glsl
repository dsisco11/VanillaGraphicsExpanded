#ifndef VGE_PBR_SHADOWCOORDS
#define VGE_PBR_SHADOWCOORDS
/// Computes terrain-relative shadow coordinates and complementary cascade coverage.
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

#endif
