#version 430
layout(local_size_x=1) in;
layout(std430,binding=0) buffer Result { vec2 result; };
layout(location=0) uniform mat4 prevViewProjMatrix;
layout(location=4) uniform sampler2D probeAnchorPosition;
layout(location=5) uniform sampler2D probeAnchorNormal;
layout(location=6) uniform sampler2D historyMeta;
layout(location=7) uniform int probeSpacing;
layout(location=8) uniform int debugMode;
layout(location=9) uniform vec2 probeGridSize;
layout(location=10) uniform float depthRejectThreshold;
layout(location=11) uniform float normalRejectThreshold;
layout(location=12) uniform float temporalAlpha;
vec3 worldToViewPos(vec3 p) { return p; }
vec3 lumonDecodeNormal(vec3 n) { return n; }
mat4 getViewMatrix() { return mat4(1); }
@import "../includes/debug/reproject_to_history.glsl"
void main(){result=reprojectToHistory(vec3(.25,-.125,-2));}
