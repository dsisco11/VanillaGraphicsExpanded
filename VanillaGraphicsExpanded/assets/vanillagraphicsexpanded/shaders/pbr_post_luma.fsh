#version 330 core
@import "./includes/postprocess_inputs.glsl"
@import "./includes/pbr_color.glsl"
uniform sampler2D sourceImage;
uniform sampler2D secondaryImage;
in vec2 uv;
layout(location=0) out vec4 outColor;
/** Retains unexposed HDR RGB and supplies exposed perceptual luma only when FXAA needs it. */
void main() {
    outColor=texture(sourceImage,uv);
    if(passInfo.z>.5) {
        float ev=passInfo.w>.5?texelFetch(secondaryImage,ivec2(0),0).r:effect.w;
        outColor.a=dot(VgeResolveDisplay(outColor.rgb*exp2(ev)),vec3(.299,.587,.114));
    }
}
