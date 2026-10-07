#version 330 core
in vec2 uv;
layout(location = 0) out vec4 outColor;
layout(binding = 0) uniform sampler2D scene;

/** Displays the selected texture without color conversion or alpha blending. */
void main()
{
    outColor = texture(scene, uv);
}
