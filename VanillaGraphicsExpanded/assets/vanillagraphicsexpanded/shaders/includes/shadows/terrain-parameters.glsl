@import "../deformation/vertex-deformation.glsl"
// One immutable draw snapshot retains the material's camera-relative deformation
// convention. The virtual view origin must match that same reference position.
layout(std140) uniform VgeShadowMaterial {
    mat4 vge_materialTransform;
    vec3 vge_chunkOrigin;
    float vge_atlasPaddingX;
    vec3 vge_deformationWorldOrigin;
    float vge_atlasPaddingY;
    float vge_animationTime;
    float vge_windPhase;
    float vge_windDetailPhase;
    float vge_liquidPhase;
    float vge_windSpeed;
    float vge_worldWarpStrength;
    float vge_glitchStrength;
    float vge_windStrength;
    float vge_liquidStrength;
    int vge_perceptionMode;
    float vge_perceptionStrength;
    float vge_materialPadding;
    mat4 vge_shadowOriginTransform;
};
/** Converts the immutable draw record into the explicit shared deformation inputs. */
VgeVertexDeformationInputs VgeShadowDeformationInputs() {
    return VgeVertexDeformationInputs(vge_deformationWorldOrigin, vge_animationTime,
        vge_windPhase, vge_windDetailPhase, vge_liquidPhase, vge_windSpeed,
        vge_worldWarpStrength, vge_glitchStrength, vge_windStrength, vge_liquidStrength,
        vge_perceptionMode, vge_perceptionStrength);
}
