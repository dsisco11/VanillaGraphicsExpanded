#version 330 core

layout(location = 0) in vec2 inAtlasCoord;
layout(location = 1) in vec4 inRadiance;

uniform vec2 atlasSize;

out vec4 vRadiance;

void main()
{
    // Place a 1px point at the target atlas texel center.
    vec2 uv = (inAtlasCoord + vec2(0.5)) / atlasSize;
    vec2 ndc = uv * 2.0 - 1.0;

    gl_Position = vec4(ndc, 0.0, 1.0);
    gl_PointSize = 1.0;

    vRadiance = inRadiance;
}
