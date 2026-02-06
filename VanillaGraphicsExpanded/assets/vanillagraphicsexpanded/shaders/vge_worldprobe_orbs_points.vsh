#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertex;
layout(location = 1) in vec4 color;
layout(location = 2) in vec2 atlasCoord;

@import "./includes/vge_worldprobe_orbs_points_params_ubo.glsl"

out vec4 vColor;
out vec2 vAtlasCoord;

void main(void)
{
    vColor = color;
    vAtlasCoord = atlasCoord;
    vec3 pos = vertex + worldOffset;
    vec4 clip = modelViewProjectionMatrix * vec4(pos, 1.0);
    gl_Position = clip;

    // Use view-axis depth (≈ -viewSpaceZ) instead of Euclidean distance so points don't
    // shrink/grow just because they're further off-center in the view frustum.
    float depth = abs(clip.w);
    float t = smoothstep(fadeNear, fadeFar, depth);
    gl_PointSize = mix(pointSize, pointSize * 0.1, t);
}
