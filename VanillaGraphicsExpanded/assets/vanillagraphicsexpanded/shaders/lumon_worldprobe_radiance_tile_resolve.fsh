#version 330 core

in vec4 vRadiance;

layout(location = 0) out vec4 outProbeRadianceAtlas;

void main()
{
    outProbeRadianceAtlas = vRadiance;
}
