#version 430 core
@import "./includes/pbr_frame_lighting.glsl"
@import "./includes/vge_frame_ubo.glsl"
@import "./includes/post_storage.glsl"
@import "./includes/post_antialias.glsl"
@import "./includes/post_local_exposure.glsl"
layout(local_size_x=1) in;
layout(std140,binding=28) uniform ExposureSnapshotInputs {
    vec4 controls; vec4 options; vec4 camera;
};
uniform sampler2D sceneImage;
uniform sampler2D localBaseImage;
layout(std430,binding=0) readonly buffer SnapshotHistory {
    float exposureEV; float reservedAdaptation; float meteredLogLuminance; float meteringValidity;
    float uncappedTarget; float exposureCapHit; float reserved0; float reserved1;
};
layout(std430,binding=1) writeonly buffer SnapshotOutput { vec4 snapshot[3]; };

/** Reuses final-display filtering and local correction at the centre presentation pixel. */
void main()
{
    vec2 uv=(floor(vgeFrame.screenSize*.5)+.5)/vgeFrame.screenSize;
    vec3 radiance=options.z>.5 ? VgeFilterDisplayEdge(sceneImage,uv,1.0/vgeFrame.screenSize).rgb
        : textureLod(sceneImage,uv,0).rgb;
    radiance=VgePostStoredLight(radiance);
    float q=0.0, base=0.0, localStops=0.0, valid=0.0;
    float applied=camera.y>.5 ? exposureEV : camera.x;
    bool guidance=VgeLocalLogLuminance(VgePhysicalFrameLight(radiance),q);
    if(options.y>.5 && guidance && VgeSampleLocalBase(localBaseImage,uv,q,options.x,base))
    {
        localStops=VgeLocalExposureStops(base,applied,camera.z,controls.xy,controls.zw);
        valid=1.0;
    }
    snapshot[0]=vec4(exposureEV,camera.z,meteredLogLuminance,meteringValidity);
    snapshot[1]=vec4(uncappedTarget,exposureCapHit,applied,localStops);
    snapshot[2]=vec4(q,base,valid,applied+localStops);
}
