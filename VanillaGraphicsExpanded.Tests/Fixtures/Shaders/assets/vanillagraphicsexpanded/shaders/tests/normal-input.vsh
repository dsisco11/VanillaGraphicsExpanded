#version 430 core
@import "../includes/tests/normal-inputs.glsl"
layout(location=0) in vec2 position;
layout(location=0) out vec3 world;
void main(){ world=vec3(backFacing!=0?-position.x:position.x,position.y,0);gl_Position=vec4(position,0,1); }
