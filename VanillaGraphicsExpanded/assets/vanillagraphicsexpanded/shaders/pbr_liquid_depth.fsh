#version 450 core
layout(location = 0) out vec4 outColor;

/** Preserves the engine liquid-depth pass's white color attachment contract. */
void main()
{
    outColor = vec4(1.0);
}
