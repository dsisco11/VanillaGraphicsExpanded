#version 430 core
layout(location=0) out vec4 color;
layout(location=1) out vec4 depth;
layout(location=4) out vec4 normal;
layout(location=5) out vec4 material;
void main() {
color=vec4(1,0,0,1); depth=vec4(0.01,0,0,1);
normal=vec4(0.5,0.5,1,-1); material=vec4(0.5,0,0,0);
}
