#version 330 core
layout(location = 0) in vec3 vertex;
layout(location = 1) in vec2 texCoord;
out vec2 uv;

/** Preserves the engine fullscreen quad's texture orientation. */
void main()
{
    gl_Position = vec4(vertex.xy, 0.0, 1.0);
    uv = texCoord;
}
