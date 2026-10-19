#ifndef VGE_LIGHTING_VOLUME_PARAMETERS
#define VGE_LIGHTING_VOLUME_PARAMETERS
layout(std140) uniform LightingVolumeInputs
{
    vec4 candidateOffsetWidth;
    vec4 candidateAnchorUnits;
    vec4 candidateFirstNodeScale;
    vec4 candidateGridCap;
};
#endif
