#ifndef VGE_CAMERA_REPROJECTION_GLSL
#define VGE_CAMERA_REPROJECTION_GLSL
/** Reconstructs a homogeneous position from bottom-left UV and OpenGL hardware depth. */
vec4 CameraReconstructPosition(mat4 inverseTransform,vec2 uv,float depth)
{
    return inverseTransform*vec4(uv*2.0-1.0,depth*2.0-1.0,1.0);
}
/** Reconstructs a camera-relative displacement, excluding the native eye translation. */
vec3 CameraReconstructDisplacement(mat4 inverseProjection,mat4 inverseView,vec2 uv,float depth)
{
    vec4 point=CameraReconstructPosition(inverseProjection,uv,depth);
    return (inverseView*vec4(point.xyz/point.w,0.0)).xyz;
}
/** Projects a render-relative position. Previous transforms must already include render-origin rebasing. */
vec4 CameraProjectPosition(mat4 viewProjection,vec3 position)
{
    return viewProjection*vec4(position,1.0);
}
/** Projects a displacement already expressed relative to the previous eye, without another camera translation. */
vec4 CameraProjectDisplacement(mat4 previousViewProjection,mat4 projection,vec3 displacement)
{
    // Rebasing and native eye translation occupy the fourth column. A direction excludes
    // that column; projection's fourth column supplies the perspective position term.
    // Callers must certify projection compatibility and apply their material motion once.
    return previousViewProjection*vec4(displacement,0.0)+projection[3];
}
/** Converts clip coordinates to bottom-left UV after the caller's homogeneous validity check. */
vec2 CameraClipUv(vec4 clip)
{
    return clip.xy/clip.w*.5+.5;
}
/** Tests finite half-open screen coverage; consumers may retain their own boundary policy. */
bool CameraUvInBounds(vec2 uv)
{
    return !any(isnan(uv))&&!any(isinf(uv))&&all(greaterThanEqual(uv,vec2(0)))&&all(lessThan(uv,vec2(1)));
}
#endif
