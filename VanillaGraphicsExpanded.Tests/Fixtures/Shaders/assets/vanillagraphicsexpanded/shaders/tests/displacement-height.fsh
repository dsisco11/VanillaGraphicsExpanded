#version 450 core
@import "../includes/tessellation/terrain_displacement.glsl"
uniform vec3 sampleInput;
            layout(location=0) out vec4 result;
            void main(){
                float h=VgeHeight(sampleInput.xy,vec2(0),vec2(.5,1),sampleInput.z,vec3(0));
                result=vec4(h,VgeEdgeLevel(vec3(-.5,0,0),vec3(.5,0,0)),VgeEdgeLevel(vec3(.5,0,0),vec3(-.5,0,0)),VgeEdgeLevel(vec3(0),vec3(0)));
            }
