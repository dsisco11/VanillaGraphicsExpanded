#version 450 core
#define VGE_PBR_ENABLE_POM 1
uniform sampler2D vge_normalDepthTex; uniform vec2 metric; out vec4 result;
@import "../includes/vge_normaldepth.glsl"
@import "../includes/vge_parallax.glsl"
void main() {
                vec2 uv=gl_FragCoord.xy/32.0;
                vec3 position=vec3((uv.x-.5)*metric.x+1.0,(uv.y-.5)*abs(metric.x),-metric.y);
                vec2 shifted=VgeApplyPomUv_WithTbn(uv,mat3(1),1,position,vec2(0),vec2(1));
                result=vec4(shifted-uv,shifted);
            }
