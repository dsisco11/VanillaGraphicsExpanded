#version 430 core
            layout(location=0) uniform vec4 colour; layout(location=1) uniform float depthValue;
            layout(location=0) out vec4 outColor; layout(location=1) out vec4 outGlow;
            void main(){outColor=colour;outGlow=vec4(.8,.7,.6,.5);gl_FragDepth=depthValue;}
