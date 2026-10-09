#version 330
@import "../includes/tests/eye_inputs.glsl"

            void main() {
                vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);
                gl_Position=vec4(p*2-1,vgeFrame.viewMatrix[3].w-1,1);
            }
