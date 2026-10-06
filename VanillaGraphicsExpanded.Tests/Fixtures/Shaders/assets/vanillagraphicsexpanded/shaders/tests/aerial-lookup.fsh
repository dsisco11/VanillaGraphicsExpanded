#version 450 core
@import "../includes/atmosphere_aerial.glsl"
uniform vec3 displacement;
uniform float visibility;
layout(location=0) out vec4 result;
void main() { result=vec4(VgeApplyAerial(vec3(1), displacement, visibility, vec2(.001,0), vec3(0,1,0)),1); }
