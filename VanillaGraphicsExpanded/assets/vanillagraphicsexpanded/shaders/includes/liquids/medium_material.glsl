#ifndef VGE_WATER_MEDIUM_MATERIAL_GLSL
#define VGE_WATER_MEDIUM_MATERIAL_GLSL
@import "./medium.glsl"

/** Fetches one compact record texel without filtering across material boundaries. */
vec4 VgeWaterRecord(int index)
{
    int width = textureSize(vge_waterMediumRecords, 0).x;
    return texelFetch(vge_waterMediumRecords, ivec2(index % width, index / width), 0);
}

/** Selects authored water properties by the original material UV, independently of animated tint sampling. */
VgeWaterMedium VgeWaterMaterial(vec2 materialUv)
{
    VgeWaterMedium medium = VgeWaterMedium(vec3(.340, .0565, .00922), vec3(0), 0.0);
    if (liquidMediumControl.x == 0.0) return medium;
    int index = int(texture(vge_waterMediumIndices, materialUv).r);
    if (index <= 0) return medium;
    vec4 absorption = VgeWaterRecord((index - 1) * 2);
    vec4 scattering = VgeWaterRecord((index - 1) * 2 + 1);
    return VgeWaterMedium(absorption.rgb, scattering.rgb, absorption.a);
}
#endif
