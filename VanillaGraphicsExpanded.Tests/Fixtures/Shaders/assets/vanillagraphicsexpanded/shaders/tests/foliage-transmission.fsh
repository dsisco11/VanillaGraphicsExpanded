#version 430 core
@import "../includes/pbr_transmission.glsl"
layout(std140, binding=28) uniform TestInputs { float visibility; vec3 lightDirection; vec3 viewDirection; };
layout(location=0) out vec4 result;
void main(){result=vec4(VgeTransmission(vec3(.2,.8,.1),vec3(0,0,1),viewDirection,lightDirection,vec3(1),0,.5,visibility),1);}
