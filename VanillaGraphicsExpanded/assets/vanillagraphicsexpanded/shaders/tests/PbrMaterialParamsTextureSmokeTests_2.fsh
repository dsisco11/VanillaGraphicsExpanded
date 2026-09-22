#version 330 core
in vec2 uv;
out vec4 outColor;

uniform sampler2D materialParams;

void main() {
    vec3 p = texture(materialParams, uv).rgb;
    outColor = vec4(p, 1.0);
}

