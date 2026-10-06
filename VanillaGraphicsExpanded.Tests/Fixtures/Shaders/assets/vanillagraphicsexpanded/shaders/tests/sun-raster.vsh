#version 430 core
@import "../includes/tests/sun_inputs.glsl"
            vec2 uvIn;
            mat4 projectionMatrix;
            mat4 viewMatrix;

@import "../includes/atmosphere_sun_vertex.glsl"
void main() {
                vec2 corners[6] = vec2[6](vec2(0,0),vec2(1,0),vec2(1,1),vec2(0,0),vec2(1,1),vec2(0,1));
                uvIn=corners[gl_VertexID];
                float zoom=.75/tan(vge_atmosphereDisk.w);
                projectionMatrix=mat4(zoom,0,0,0, 0,zoom,0,0, 0,0,-1,-1, 0,0,-1,0);
                viewMatrix=mat4(1); viewMatrix[3]=vec4(camera,1);
                VgeDrawAtmosphericSun();
            }
