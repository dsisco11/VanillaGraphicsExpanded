#ifndef VGE_CLOUD_REPROJECTION
#define VGE_CLOUD_REPROJECTION
/** Reconstructs camera-relative displacement without applying native view translation twice. */
vec3 CloudTemporalPoint(vec2 uv,float depth)
{
    vec4 p=vgeFrame.invProjectionMatrix*vec4(uv*2.0-1.0,depth*2.0-1.0,1);
    return (vgeFrame.invViewMatrix*vec4(p.xyz/p.w,0)).xyz;
}
#endif
