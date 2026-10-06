#version 330 core
            layout(location=0) uniform sampler2D ordinaryTexture;
            layout(location=0) out vec4 color;
            void main() { color = texture(ordinaryTexture, vec2(.5)); }
