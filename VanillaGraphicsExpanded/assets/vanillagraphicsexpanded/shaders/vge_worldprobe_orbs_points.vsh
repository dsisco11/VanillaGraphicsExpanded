#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertex;
layout(location = 1) in vec4 color;
layout(location = 2) in vec2 atlasCoord;

uniform mat4 modelViewProjectionMatrix;
uniform float pointSize;

out vec4 vColor;
out vec2 vAtlasCoord;

void main(void)
{
    vColor = color;
    vAtlasCoord = atlasCoord;
    gl_Position = modelViewProjectionMatrix * vec4(vertex, 1.0);
    gl_PointSize = pointSize;
}

