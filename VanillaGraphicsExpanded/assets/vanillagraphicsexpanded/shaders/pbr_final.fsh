#version 330 core
@import "./includes/pbr_color.glsl"
@import "./includes/post_antialias.glsl"
@import "./includes/post_grading.glsl"
layout(std140) uniform FinalDisplayInputs {
    vec4 frame; vec4 grading; vec4 effects; vec4 vignette; vec4 exposure;
};
uniform sampler2D sceneImage;
uniform sampler2D bloomImage;
uniform sampler2D shaftImage;
uniform sampler2D exposureImage;
in vec2 uv;
layout(location=0) out vec4 outColor;
/** Composes HDR effects, applies one camera/display transform, then grades and dithers the display result. */
void main()
{
    vec3 radiance=frame.z>.5?VgeFilterDisplayEdge(sceneImage,uv,frame.xy).rgb:texture(sceneImage,uv).rgb;
    radiance+=texture(bloomImage,uv).rgb+texture(shaftImage,uv).rgb;
    float ev=exposure.y>.5?texelFetch(exposureImage,ivec2(0),0).r:exposure.x;
    vec3 display=VgeResolveDisplay(radiance*exp2(ev));
    display=VgeGradeDisplay(display,grading,effects.x);
    display=VgeScreenEffects(display,uv,gl_FragCoord.xy,effects,vignette);
    outColor=vec4(VgeDitherFinalDisplay(display,gl_FragCoord.xy),1.0);
}
