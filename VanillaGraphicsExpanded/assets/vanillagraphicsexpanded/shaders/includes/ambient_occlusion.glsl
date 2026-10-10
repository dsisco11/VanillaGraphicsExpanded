#ifndef VGE_AMBIENT_OCCLUSION
#define VGE_AMBIENT_OCCLUSION
@import "./vge_frame_ubo.glsl"
layout(std140, binding = 28) uniform AmbientOcclusionInputs {
    vec4 aoFrame; // reserved.xy, reduction divisor, operation
    vec4 aoSampling; // world radius, dimensionless horizon relaxation [0,1], slices, radial steps
    vec4 aoDistance; // fade start/end, reserved
};
/** Reconstructs the receiver in view space from OpenGL hardware depth. */
vec3 VgeAoPosition(vec2 uv, float depth) {
    vec4 p=vgeFrame.invProjectionMatrix*vec4(uv*2.0-1.0,depth*2.0-1.0,1.0);
    return p.xyz/p.w;
}
/** Stores a unit normal with two signed octahedral coordinates. */
vec2 VgeAoEncodeNormal(vec3 n) {
    n/=abs(n.x)+abs(n.y)+abs(n.z);
    return n.z>=0.0?n.xy:(1.0-abs(n.yx))*mix(vec2(-1),vec2(1),greaterThanEqual(n.xy,vec2(0)));
}
/** Restores the normal used to reject unrelated spatial samples. */
vec3 VgeAoDecodeNormal(vec2 e) {
    vec3 n=vec3(e,1.0-abs(e.x)-abs(e.y));
    n.xy+=mix(vec2(1),vec2(-1),greaterThanEqual(n.xy,vec2(0)))*max(-n.z,0.0);
    return normalize(n);
}
/** Raises cosine-space occlusion immediately and relaxes weaker later evidence by a bounded fraction. */
float VgeAoRelaxHorizon(float horizon, float candidate, float relaxation) {
    return candidate>=horizon?candidate:mix(horizon,candidate,clamp(relaxation,0.0,1.0));
}
/** Integrates cosine-weighted visibility analytically between signed slice horizons. */
float VgeAoIntegral(float low, float high, float normalAngle) {
    float c=cos(normalAngle),s=sin(normalAngle);
    return 0.25*(2.0*c+2.0*(low+high)*s-cos(2.0*low-normalAngle)-cos(2.0*high-normalAngle));
}
#endif
