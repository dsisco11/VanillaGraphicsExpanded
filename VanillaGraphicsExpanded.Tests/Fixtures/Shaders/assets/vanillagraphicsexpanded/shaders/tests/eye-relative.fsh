#version 450 core
@import "../includes/tests/eye_inputs.glsl"
#define VGE_PBR_ENABLE_POM 1
uniform sampler2D vge_normalDepthTex;


out vec4 color;
@import "../includes/vge_normaldepth.glsl"
@import "../includes/vge_parallax.glsl"
void main() {
                vec3 toEye=VgeFragmentToEyeWorld(surface);
                if(outputMode==0) { color=vec4(toEye,length(toEye)); return; }
                vec2 baseUv=vec2(.5)+(gl_FragCoord.xy-vec2(.5))*.01;
                vec3 metricSurface=surface+vec3((gl_FragCoord.xy-vec2(.5))*.01,0);
                vec2 uv=VgeApplyPomUv_WithTbn(baseUv,mat3(1),1,metricSurface,vec2(0),vec2(1));
                vec4 n=VgeComputePackedWorldNormal01Height01_WithTbn(vec2(.5),vec3(0,0,1),surface,mat3(1),1,vec3(.6,0,.8),.75);
                color=vec4(uv,n.x,n.z);
            }
