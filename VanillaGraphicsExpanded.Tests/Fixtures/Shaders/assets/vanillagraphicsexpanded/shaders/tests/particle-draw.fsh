#version 430 core
layout(std140, binding = 28) uniform ParticleDrawInputs
{
    vec4 colour;
    float depthValue;
};

            layout(location=0) out vec4 outColor; layout(location=1) out vec4 outGlow;
            void main(){outColor=colour;outGlow=vec4(.8,.7,.6,.5);gl_FragDepth=depthValue;}
