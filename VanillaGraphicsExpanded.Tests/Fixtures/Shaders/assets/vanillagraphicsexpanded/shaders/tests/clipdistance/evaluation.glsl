#version 450
layout(triangles) in;
out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[1]; };
void main(){gl_Position=gl_in[0].gl_Position;gl_ClipDistance[0]=1;}
