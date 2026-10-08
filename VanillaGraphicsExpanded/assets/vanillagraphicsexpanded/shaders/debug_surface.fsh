#version 330 core
in vec2 uv;
layout(location = 0) out vec4 outColor;
uniform sampler2DArray scene;
/** Displays one owned G-buffer layer without a staging copy. */
void main()
{
#if VGE_DEBUG_MATERIAL
    outColor = texture(scene, vec3(uv, 1));
#else
    outColor = texture(scene, vec3(uv, 0));
#endif
}
