#version 330 core
@import "./includes/postprocess_inputs.glsl"
uniform sampler2D sourceImage;
uniform sampler2D secondaryImage;
in vec2 uv;
layout(location=0) out vec4 outColor;
/** Uses normalized, symmetric taps measured in the sampled image's own texels. */
vec3 box4(vec2 p) {
    vec2 t=1.0/vec2(textureSize(sourceImage,0));
    return (texture(sourceImage,p+t*vec2(-.5,-.5)).rgb+texture(sourceImage,p+t*vec2(.5,-.5)).rgb
        +texture(sourceImage,p+t*vec2(-.5,.5)).rgb+texture(sourceImage,p+t*vec2(.5,.5)).rgb)*.25;
}
/** Extracts radiance with an exposure-relative soft knee, or reconstructs a normalized bloom pyramid. */
void main() {
    vec3 color;
    if(passInfo.z<1.5) {
        color=max(box4(uv),vec3(0));
        if(passInfo.z<.5) {
            float ev=passInfo.w>.5?texelFetch(secondaryImage,ivec2(0),0).r:effect.w;
            float threshold=effect.x/exp2(ev), knee=max(threshold*effect.y,1e-6);
            float peak=max(color.r,max(color.g,color.b));
            float soft=clamp(peak-threshold+knee,0.0,2.0*knee);
            float contribution=max(peak-threshold,soft*soft/(4.0*knee));
            color*=max(contribution,0.0)/max(peak,1e-6)*effect.z;
        }
    } else {
        vec2 t=1.0/vec2(textureSize(secondaryImage,0));
        vec3 low=vec3(0);
        for(int y=-1;y<=1;++y) for(int x=-1;x<=1;++x)
            low+=texture(secondaryImage,uv+vec2(x,y)*t).rgb*float((x==0?2:1)*(y==0?2:1))/16.0;
        color=mix(texture(sourceImage,uv).rgb,low,.5);
    }
    outColor=vec4(color,1);
}
