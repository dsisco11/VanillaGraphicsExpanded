#ifndef VGE_PHOTOMETRIC_GLSL
#define VGE_PHOTOMETRIC_GLSL
@import "./vge_frame_ubo.glsl"
const float VGE_PHOTOMETRIC_PI = 3.14159265358979323846;
const float VGE_PERSISTENT_LIGHT_SCALE = 0.0625;
/** Converts only the propagated engine block RGB field to incident lux, without color transfer or exposure. */
vec3 VgeBlockIlluminance(vec3 legacyBlockRgb)
{
    vec3 block = max(legacyBlockRgb, vec3(0));
    float peak = max(block.r, max(block.g, block.b));
    return peak > 0.0 ? block * (max(peak - vgeFrame.lightCalibration.y, 0.0) / peak)
        * vgeFrame.lightCalibration.x : vec3(0);
}
/** Converts diffuse incident illuminance to the integrated E/pi input before receiver reflectance. */
vec3 VgeDiffuseIncident(vec3 illuminanceLux) { return illuminanceLux / VGE_PHOTOMETRIC_PI; }
/** Encodes admitted persistent lighting; metadata and dimensionless material quantities bypass this conversion. */
vec3 VgeEncodePersistentLight(vec3 physical) { return physical * VGE_PERSISTENT_LIGHT_SCALE; }
/** Decodes persistent lux or nits before physical shading. */
vec3 VgeDecodePersistentLight(vec3 stored) { return stored / VGE_PERSISTENT_LIGHT_SCALE; }
/** Checks scalar/signed-coefficient admission before a persistent write is marked valid. */
bool VgePersistentLightAdmitted(vec3 physical, bool signedCoefficients)
{
    return !any(isnan(physical)) && !any(isinf(physical))
        && all(lessThanEqual(abs(physical), vec3(1048064)))
        && (signedCoefficients || all(greaterThanEqual(physical, vec3(0))));
}
/** Admits nonnegative outgoing radiance before the qualified signed SH9 projection. */
bool VgeDirectionalLightAdmitted(vec3 physicalNits)
{
    return !any(isnan(physicalNits)) && !any(isinf(physicalNits))
        && all(greaterThanEqual(physicalNits, vec3(0))) && all(lessThanEqual(physicalNits, vec3(131072)));
}
#endif
