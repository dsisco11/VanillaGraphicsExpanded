#version 430 core
            layout(location=0) out vec4 color;layout(location=1) out vec4 glow;
            void main(){color=vec4(8,4,2,.5);glow=vec4(1);gl_FragDepth=.5;}
