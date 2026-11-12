// Frozen native terrain policy; the same shared stage bodies determine edge levels and height.
layout(std140) uniform VgeShadowSubdivision {
    vec2 vge_tessellationPixels;
    vec2 vge_tessellationDistance;
    float vge_tessellationFocalPixels;
    int vge_displacementEnabled;
    vec2 vge_subdivisionPadding;
};
