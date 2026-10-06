#version 330 core
layout(location=0) out float modelY; layout(location=0) uniform int lineMode; layout(location=1) uniform int zeroDepth; void main(){vec2 p; if(lineMode!=0) p=vec2(gl_VertexID==0?-1.0:1.0,0.0);else p=vec2((gl_VertexID==1)?3.0:-1.0,(gl_VertexID==2)?3.0:-1.0);gl_Position=vec4(p,zeroDepth!=0?0.25:-0.5,1.0);gl_ClipDistance[0]=p.x;modelY=p.y;}
