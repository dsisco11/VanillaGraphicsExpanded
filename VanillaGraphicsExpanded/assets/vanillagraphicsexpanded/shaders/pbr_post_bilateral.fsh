#version 330 core
@import "./includes/postprocess_inputs.glsl"
uniform sampler2D sourceImage;
uniform sampler2D secondaryImage;
in vec2 uv;
layout(location=0) out vec4 outColor;
/** Preserves the engine's depth-aware SSAO filter, with all eleven symmetric taps initialized. */
void main() {
    const float kernel[6]=float[6](.231613,.195779,.118235,.051008,.015715,.003456);
    vec2 delta=passInfo.z>.5?vec2(0,passInfo.y):vec2(passInfo.x,0);
    float depth=texture(secondaryImage,uv).r, total=0.0;
    vec4 value=vec4(0);
    for(int i=-5;i<=5;++i) {
        vec2 p=uv+delta*float(i);
        float weight=kernel[abs(i)]*(1.0-clamp(abs(texture(secondaryImage,p).r-depth)*300.0,0.0,1.0));
        value+=texture(sourceImage,p)*weight; total+=weight;
    }
    outColor=value/total;
}
