#version 330 core
layout(location=0) in float modelY; layout(location=0) out vec4 result;void main(){result=vec4(1,modelY>0.0?1.0:0.0,1,0.5);}
