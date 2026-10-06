#version 450
layout(constant_id=7) const int count=2;
out float gl_ClipDistance[count+1];
void main(){gl_Position=vec4(0,0,0,1);for(int i=0;i<count+1;i++) gl_ClipDistance[i]=1;}
