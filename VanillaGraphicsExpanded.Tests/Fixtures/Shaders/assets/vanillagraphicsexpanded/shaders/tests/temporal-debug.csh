#version 430
#extension GL_ARB_shading_language_420pack : require
@import "../includes/vge_frame_ubo.glsl"
layout(local_size_x=1) in;
layout(std430,binding=0) buffer Result { vec2 result; };

@import "../includes/debug/reproject_to_history.glsl"
/** Executes production reprojection using only the shared frame and explicit result buffer. */
void main(){result=reprojectToHistory(vec3(.25,-.125,-2));}
