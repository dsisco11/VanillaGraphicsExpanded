#version 330 core

in vec2 uv;
out vec4 outColor;

// SSGI Debug Shader
// Simply displays the SSGI buffer directly to screen for debugging

uniform sampler2D ssgiTexture;
uniform float boost;

void main() {
    vec3 ssgi = texture(ssgiTexture, uv).rgb;
    
    // Apply boost for visibility and output
    outColor = vec4(ssgi * boost, 1.0);
}
