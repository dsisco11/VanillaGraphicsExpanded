#version 430 core
@import "../includes/tests/sun_inputs.glsl"
            vec2 uvIn;

@import "../includes/atmosphere_sun_vertex.glsl"
void main() {
                vec2 corners[6] = vec2[6](vec2(0,0),vec2(1,0),vec2(1,1),vec2(0,0),vec2(1,1),vec2(0,1));
                uvIn=corners[gl_VertexID];
                VgeDrawAtmosphericSun();
            }
