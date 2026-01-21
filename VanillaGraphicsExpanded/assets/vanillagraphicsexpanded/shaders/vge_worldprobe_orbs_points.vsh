#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertex;
layout(location = 1) in vec4 color;
layout(location = 2) in vec2 atlasCoord;

uniform mat4 modelViewProjectionMatrix;
uniform float pointSize;
uniform float fadeNear;
uniform float fadeFar;

out vec4 vColor;
out vec2 vAtlasCoord;

void main(void)
{
    vColor = color;
    vAtlasCoord = atlasCoord;
    gl_Position = modelViewProjectionMatrix * vec4(vertex, 1.0);
    float dist = length(vertex);
    float t = smoothstep(fadeNear, fadeFar, dist);
    gl_PointSize = mix(pointSize, pointSize * 0.1, t);
}
