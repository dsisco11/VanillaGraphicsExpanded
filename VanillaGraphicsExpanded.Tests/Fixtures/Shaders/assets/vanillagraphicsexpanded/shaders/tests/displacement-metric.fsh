#version 450 core
@import "../includes/tests/displacement_inputs.glsl"
@import "../includes/tessellation/terrain_displacement.glsl"

            out vec4 result;
            void main(){vec3 a=vec3(-.5,0,distance),b=vec3(.5,0,distance);result=vec4(VgeEdgeLevel(a,b),VgeEdgeLevel(b,a),VgeEdgeLevel(a,a),1);}
