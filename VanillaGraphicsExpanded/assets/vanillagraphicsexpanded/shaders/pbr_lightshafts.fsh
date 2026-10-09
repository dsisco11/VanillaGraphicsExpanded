#version 330 core
@import "./includes/postprocess_inputs.glsl"
@import "./includes/vge_frame_ubo.glsl"
uniform sampler2D sourceImage;
uniform sampler2D visibilityImage;
uniform sampler2D depthImage;
uniform sampler2D exposureImage;
in vec2 uv;
layout(location=0) out vec4 outColor;
/** Bounds source support smoothly in aspect-correct screen coordinates. */
float sourceSupport(vec2 p) {
    float radius=length((p-sun.xy)*vec2(sun.w,1.0));
    float edge=min(min(p.x,p.y),min(1.0-p.x,1.0-p.y));
    return (1.0-smoothstep(0.12,0.3,radius))*smoothstep(0.0,0.04,edge);
}
/** Extracts bounded, unexposed shaft bloom and an independent distance-based sky visibility. */
vec4 extractSource(vec2 p) {
    float depth=texture(depthImage,p).r;
    float distance=abs(vgeFrame.projectionMatrix[3][2]/max(abs(depth*2.0-1.0+vgeFrame.projectionMatrix[2][2]),1e-6));
    float visibility=smoothstep(0.0,max(solar.w,1.0),distance);
    float ev=passInfo.w>0.5?texelFetch(exposureImage,ivec2(0),0).r:effect.w;
    float exposureScale=exp2(clamp(ev,-24.0,24.0));
    vec3 scene=max(texture(sourceImage,p).rgb,vec3(0));
    float luminance=dot(scene,vec3(0.2126,0.7152,0.0722));
    float exposed=luminance*exposureScale;
    float excess=max(exposed-effect.x,0.0);
    vec3 bloom=scene*(excess/max(exposed,1e-6));
    // Preserve hue while bounding the oversized solar disk before radial filtering.
    float peak=max(bloom.r,max(bloom.g,bloom.b));
    bloom*=min(1.0,effect.y/max(peak*exposureScale*max(effect.z,1.0),1e-6));
    float sourceMask=clamp(texture(visibilityImage,p).g,0.0,1.0)*step(0.9999,depth);
    bloom*=solar.rgb*effect.z*sun.z*sourceMask*sourceSupport(p);
    return vec4(bloom,visibility);
}
/** Normalized radial filtering preserves constant fields and treats outside-screen occlusion as unknown/clear. */
vec4 filterSource() {
    int count=clamp(int(passInfo.w),1,64);
    vec4 total=vec4(0);
    for(int i=0;i<64;++i) {
        if(i>=count) break;
        float offset=effect.x*float(i)/float(max(count-1,1));
        vec2 p=mix(uv,sun.xy,offset);
        bool inside=all(greaterThanEqual(p,vec2(0)))&&all(lessThanEqual(p,vec2(1)));
        total+=inside?texture(sourceImage,p):vec4(0,0,0,1);
    }
    return total/float(count);
}
/** Executes extraction, radial filtering, or independent bloom/occlusion publication. */
void main() {
    if(sun.z<=0.0) { outColor=vec4(0,0,0,1); return; }
    if(passInfo.z>3.5) {
        float depth=texture(depthImage,uv).r;
        float distance=abs(vgeFrame.projectionMatrix[3][2]/max(abs(depth*2.0-1.0+vgeFrame.projectionMatrix[2][2]),1e-6));
        outColor=vec4(0,0,0,smoothstep(0.0,max(solar.w,1.0),distance));
    } else if(passInfo.z<0.5) {
        // Four balanced samples retain small sources without directional downsample bias.
        vec2 texel=0.5*vec2(length(dFdx(uv)),length(dFdy(uv)));
        outColor=(extractSource(uv+texel*vec2(-0.5,-0.5))+extractSource(uv+texel*vec2(0.5,-0.5))
                 +extractSource(uv+texel*vec2(-0.5,0.5))+extractSource(uv+texel*vec2(0.5,0.5)))*0.25;
    } else if(passInfo.z<1.5) outColor=filterSource();
    else if(passInfo.z<2.5) outColor=vec4(texture(sourceImage,uv).rgb,1);
    else {
        float visibility=mix(1.0,clamp(texture(sourceImage,uv).a,0.0,1.0),sun.z);
        outColor=vec4(vec3(1.0-visibility),1);
    }
}
