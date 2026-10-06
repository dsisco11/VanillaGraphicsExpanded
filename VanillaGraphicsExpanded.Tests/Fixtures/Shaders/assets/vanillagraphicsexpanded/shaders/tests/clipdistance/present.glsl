#version 450
out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[3]; };
void main(){gl_Position=vec4(0,0,0,1);for(int i=0;i<3;i++) gl_ClipDistance[i]=1;}
