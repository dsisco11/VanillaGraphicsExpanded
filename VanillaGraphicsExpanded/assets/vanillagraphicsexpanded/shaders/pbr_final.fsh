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
    vec4 localControls; vec4 localOptions;
};
#if VGE_LOCAL_EXPOSURE
@import "./includes/post_local_exposure.glsl"
layout(std430,binding=1) readonly buffer CameraAdaptation {
    float adaptationExposure; float adaptedLogLuminance; float meteredLogLuminance;
    float adaptationValidity; float uncappedTarget; float exposureCapHit;
};
uniform sampler2D localBaseImage;
#endif
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
    float localStops=0.0;
#if VGE_LOCAL_EXPOSURE
    float q, base;
    // Independent finite masks keep neutral policy separate from the shader's
    // conditional guidance evaluation, including corrupt validity metadata.
    float localMask=float(localOptions.y>.5)*float(adaptationValidity>.5)
        *float(!isnan(adaptationValidity))*float(!isinf(adaptationValidity));
    // Guidance comes from scene radiance alone; generated glare cannot alter
    // the viewing estimate. Its final contribution shares the same local gain.
    if(localMask>.5)
    {
        if(VgeLocalLogLuminance(VgePhysicalFrameLight(radiance),q)
            && VgeSampleLocalBase(localBaseImage,uv,q,localOptions.x,base))
            localStops=VgeLocalExposureStops(base,adaptedLogLuminance,localControls.xy,localControls.zw);
    }
    // Preserve an explicit neutral selection at the display boundary, including
    // the optional executable when its runtime controls are disabled or missing.
    localStops*=localMask;
#endif
    radiance+=VgePostStoredLight(texture(bloomImage,uv).rgb)+VgePostStoredLight(texture(shaftImage,uv).rgb);
    float ev=exposure.y>.5?cameraExposureEV:exposure.x;
    // Three bounded half-float inputs and finite 1/P keep the FP32 sum/product
    // finite even at the highest admitted display exposure. Do not cap this sum to half.
    float size=float(textureSize(gradedDisplayLookup,0).x);
    vec3 lookup=VgeGradingCoordinate(radiance*VgePostExposure(ev)*exp2(localStops));
    vec3 display=texture(gradedDisplayLookup,(lookup*(size-1.0)+.5)/size).rgb;
    display=VgeDisplayTint(display,effects.x);
    display=VgeScreenEffects(display,uv,gl_FragCoord.xy,effects,vignette);
#if VGE_LOCAL_EXPOSURE
    if(localOptions.y>.5 && localOptions.z>.5)
    {
        float amount=min(abs(localStops)/8.0,1.0);
        vec3 endpoint=localStops>=0.0 ? vec3(1,0,0) : vec3(0,0,1);
        display=mix(vec3(.5),endpoint,amount);
    }
#endif
    outColor=vec4(VgeDitherFinalDisplay(display,gl_FragCoord.xy),1.0);
}
