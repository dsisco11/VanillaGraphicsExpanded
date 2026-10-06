#version 450 core
layout(std140, binding = 28) uniform AerialLookupInputs
{
    vec3 displacement;
    float visibility;
};
@import "../includes/atmosphere_aerial.glsl"


layout(location=0) out vec4 result;
void main() { result=vec4(VgeApplyAerial(vec3(1), displacement, visibility, vec2(.001,0), vec3(0,1,0)),1); }
