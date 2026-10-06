#version 450
layout(triangles) in;layout(points,max_vertices=1) out;
out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[2]; };
void main(){gl_Position=gl_in[0].gl_Position;gl_ClipDistance[0]=1;gl_ClipDistance[1]=1;EmitVertex();EndPrimitive();}
