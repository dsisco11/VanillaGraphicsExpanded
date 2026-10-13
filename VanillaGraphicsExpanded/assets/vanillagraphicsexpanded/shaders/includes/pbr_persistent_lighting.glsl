#ifndef VGE_PERSISTENT_LIGHTING_GLSL
#define VGE_PERSISTENT_LIGHTING_GLSL
@import "./pbr_storage_safety.glsl"
const float VGE_PERSISTENT_LIGHT_SCALE = 0.0625;
/** Encodes admitted persistent lux, nits or signed coefficients without scaling metadata. */
vec3 VgeEncodePersistentLight(vec3 physical) { return physical * VGE_PERSISTENT_LIGHT_SCALE; }
/** Restores the declared physical quantity before material response or angular integration. */
vec3 VgeDecodePersistentLight(vec3 stored) { return stored / VGE_PERSISTENT_LIGHT_SCALE; }
/** Rejects unsupported physical updates before encoding or granting publication validity. */
bool VgePersistentLightAdmitted(vec3 physical, bool signedCoefficients)
{
    return !any(isnan(physical)) && !any(isinf(physical))
        && all(lessThanEqual(abs(physical), vec3(1048064)))
        && (signedCoefficients || all(greaterThanEqual(physical, vec3(0))));
}
/** Restricts directional inputs to the separately qualified SH projection domain. */
bool VgeDirectionalLightAdmitted(vec3 physicalNits)
{
    return !any(isnan(physicalNits)) && !any(isinf(physicalNits))
        && all(greaterThanEqual(physicalNits, vec3(0))) && all(lessThanEqual(physicalNits, vec3(131072)));
}
/** Validates already encoded directional history before interpolation, retaining its unscaled metadata. */
bool VgeStoredDirectionalLightAdmitted(vec3 stored)
{
    return VgeDirectionalLightAdmitted(VgeDecodePersistentLight(stored));
}
/** Protects signed fixed-scale coefficients before binary16 narrowing; containment never grants validity. */
bool VgeProtectPersistentCoefficient(inout vec3 stored)
{
    vec3 protectedValue;
    bool clipped;
    bool valid = VgeProtectLightingStorage(stored, -VGE_STORAGE_HALF_MAX, VGE_STORAGE_HALF_MAX, protectedValue, clipped);
    stored = protectedValue;
    return valid && !clipped;
}
/** Checks admission and defensive binary16 protection independently; clipping never becomes valid history. */
bool VgeTryStorePersistentLight(vec3 physical, bool signedCoefficients, out vec3 stored)
{
    stored = vec3(0);
    if (!VgePersistentLightAdmitted(physical, signedCoefficients)) return false;
    bool clipped;
    bool valid = VgeProtectLightingStorage(VgeEncodePersistentLight(physical),
        signedCoefficients ? -VGE_STORAGE_HALF_MAX : 0.0, VGE_STORAGE_HALF_MAX, stored, clipped);
    return valid && !clipped;
}
#endif
