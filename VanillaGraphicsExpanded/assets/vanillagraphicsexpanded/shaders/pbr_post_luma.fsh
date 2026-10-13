#version 330 core
#extension GL_ARB_shader_storage_buffer_object : require
#extension GL_ARB_shader_image_load_store : require
@import "./includes/camera_exposure_buffer.glsl"
@import "./includes/pbr_frame_lighting.glsl"
@import "./includes/post_storage.glsl"
@import "./includes/postprocess_inputs.glsl"
@import "./includes/pbr_color.glsl"
uniform sampler2D sourceImage;
in vec2 uv;
layout(location=0) out vec4 outColor;
/** Retains unexposed HDR RGB and supplies exposed perceptual luma only when FXAA needs it. */
void main() {
    outColor=texture(sourceImage,uv);
    outColor.rgb=VgePostStoredLight(outColor.rgb);
    if(passInfo.z>.5) {
        float ev=passInfo.w>.5?cameraExposureEV:effect.w;
        outColor.a=dot(VgeResolveDisplay(outColor.rgb*VgePostExposure(ev)),vec3(.299,.587,.114));
    }
}
