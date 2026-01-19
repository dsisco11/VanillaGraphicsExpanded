#version 330 core

layout(location = 0) in vec2 inAtlasCoord;
layout(location = 1) in vec4 inShR;
layout(location = 2) in vec4 inShG;
layout(location = 3) in vec4 inShB;
layout(location = 4) in vec3 inAoDirWorld;
layout(location = 5) in float inAoConfidence;
layout(location = 6) in float inConfidence;
layout(location = 7) in float inMeanLogHitDistance;
layout(location = 8) in uint inFlags;

uniform vec2 atlasSize;

out vec4 vShR;
out vec4 vShG;
out vec4 vShB;
out vec3 vAoDirWorld;
out float vAoConfidence;
out float vConfidence;
out float vMeanLogHitDistance;
flat out uint vFlags;

void main()
{
    // Place a 1px point at the target atlas texel center.
    vec2 uv = (inAtlasCoord + vec2(0.5)) / atlasSize;
    vec2 ndc = uv * 2.0 - 1.0;

    gl_Position = vec4(ndc, 0.0, 1.0);
    gl_PointSize = 1.0;

    vShR = inShR;
    vShG = inShG;
    vShB = inShB;
    vAoDirWorld = inAoDirWorld;
    vAoConfidence = inAoConfidence;
    vConfidence = inConfidence;
    vMeanLogHitDistance = inMeanLogHitDistance;
    vFlags = inFlags;
}
