#version 450 core
@import "vanillagraphicsexpanded:shaders/includes/oit.glsl"

layout(std140, binding = 28) uniform OitOwnedWriterInputs
{
    vec4 testColor;
    float testGlow;
};

/** Sends captured color and glow through the shipped OIT writer without changing its MRT outputs. */
void main()
{
    writeOit(testColor, testGlow);
}
