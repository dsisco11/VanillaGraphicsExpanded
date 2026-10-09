#version 430
#extension GL_ARB_shading_language_420pack : require
@import "../includes/vge_frame_ubo.glsl"
layout(local_size_x=1) in;
layout(std430,binding=0) buffer Result { vec2 result; };

layout(location=4) uniform sampler2D probeAnchorPosition;
layout(location=5) uniform sampler2D probeAnchorNormal;
layout(location=6) uniform sampler2D historyMeta;






vec3 worldToViewPos(vec3 p) { return p; }
vec3 lumonDecodeNormal(vec3 n) { return n; }
mat4 getViewMatrix() { return mat4(1); }
@import "../includes/debug/reproject_to_history.glsl"
void main(){result=reprojectToHistory(vec3(.25,-.125,-2));}
