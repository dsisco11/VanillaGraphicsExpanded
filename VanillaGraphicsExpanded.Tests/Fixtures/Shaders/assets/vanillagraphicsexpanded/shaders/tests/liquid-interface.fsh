#version 430 core
const float zNear=.1;const float zFar=100.0;
@import "../includes/pbr_liquid_optics.glsl"
layout(std140,binding=28) uniform TestInputs {float cosine;bool underwater;};
layout(location=0) out vec4 result;
void main(){result=vec4(VgeLiquidFresnel(cosine,underwater),VgeLiquidThickness(1.0,.5,vec3(0,0,-1),underwater),VgeLiquidThickness(.4,.5,vec3(0,0,-1),underwater),VgeLiquidThickness(.75,.5,vec3(0,0,-1),underwater));}
