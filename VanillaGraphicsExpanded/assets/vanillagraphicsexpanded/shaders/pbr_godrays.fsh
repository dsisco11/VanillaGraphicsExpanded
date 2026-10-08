#version 330 core
@import "./includes/postprocess_inputs.glsl"
uniform sampler2D visibilityImage;
uniform sampler2D depthImage;
in vec2 uv;
layout(location=0) out vec4 outColor;
/** Integrates a bounded solar visibility mask; never radially blurs unbounded HDR scene radiance. */
void main() {
    if(sun.z<=0.0) { outColor=vec4(0,0,0,1); return; }
    int samples=int(effect.x);
    vec2 stepUv=(sun.xy-uv)*effect.y/float(samples);
    float sum=0.0, weights=0.0;
    for(int i=0;i<64;++i) {
        if(i>=samples) break;
        vec2 p=uv+stepUv*(float(i)+.5);
        float weight=exp(-3.0*(float(i)+.5)/float(samples));
        // Outside-screen samples contribute zero, without edge clamping streaks.
        if(all(greaterThanEqual(p,vec2(0)))&&all(lessThanEqual(p,vec2(1)))) {
            float visible=clamp(texture(visibilityImage,p).g,0.0,1.0);
            visible*=step(.9999,texture(depthImage,p).r);
            sum+=visible*weight;
        }
        weights+=weight;
    }
    outColor=vec4(solar.rgb*sun.z*sum/max(weights,1e-6),1);
}
