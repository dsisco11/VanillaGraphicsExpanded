#version 330 core
@import "./includes/pbr_persistent_lighting.glsl"

in vec4 vRadiance;

layout(location = 0) out vec4 outProbeRadianceAtlas;

/** Encodes physical worker answers while leaving directional validity/distance unchanged. */
void main()
{
    vec3 stored;
    bool admitted = VgeDirectionalLightAdmitted(vRadiance.rgb)
        && VgeTryStorePersistentLight(vRadiance.rgb, false, stored);
    outProbeRadianceAtlas = admitted ? vec4(stored, vRadiance.a) : vec4(0);
}
