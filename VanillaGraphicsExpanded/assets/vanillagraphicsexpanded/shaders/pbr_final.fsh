#version 330 core
#extension GL_ARB_shader_storage_buffer_object : require
#extension GL_ARB_shader_image_load_store : require
@import "./includes/camera_exposure_buffer.glsl"
@import "./includes/pbr_frame_lighting.glsl"
@import "./includes/post_storage.glsl"
@import "./includes/vge_frame_ubo.glsl"
@import "./includes/pbr_color.glsl"
@import "./includes/post_antialias.glsl"
@import "./includes/post_grading.glsl"
@import "./includes/post_filmic.glsl"
layout(std140, binding = 28) uniform FinalDisplayInputs {
    vec4 grading; vec4 effects; vec4 vignette; vec4 exposure;
};
uniform sampler2D sceneImage;
uniform sampler2D bloomImage;
uniform sampler2D shaftImage;
uniform sampler3D gradedDisplayLookup;
in vec2 uv;
layout(location=0) out vec4 outColor;
/** Composes HDR effects and exposure before cached scene grading, film response and display encoding. */
void main()
{
    vec3 radiance=exposure.z>.5?VgeFilterDisplayEdge(sceneImage,uv,1.0/vgeFrame.screenSize).rgb:texture(sceneImage,uv).rgb;
    radiance=VgePostStoredLight(radiance);
    radiance+=VgePostStoredLight(texture(bloomImage,uv).rgb)+VgePostStoredLight(texture(shaftImage,uv).rgb);
    float ev=exposure.y>.5?cameraExposureEV:exposure.x;
    // Three bounded half-float inputs and finite 1/P keep the FP32 sum/product
    // finite even at the highest admitted display exposure. Do not cap this sum to half.
    float size=float(textureSize(gradedDisplayLookup,0).x);
    vec3 lookup=VgeGradingCoordinate(radiance*VgePostExposure(ev));
    vec3 display=texture(gradedDisplayLookup,(lookup*(size-1.0)+.5)/size).rgb;
    display=VgeDisplayTint(display,effects.x);
    display=VgeScreenEffects(display,uv,gl_FragCoord.xy,effects,vignette);
    outColor=vec4(VgeDitherFinalDisplay(display,gl_FragCoord.xy),1.0);
}
