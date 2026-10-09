@import "./vge_frame_ubo.glsl"

#ifndef VGE_ATMOSPHERE_SUN_INPUTS
uniform int vge_atmosphereSunDraw;
uniform vec4 vge_atmosphereSun; // admitted direction, planetary horizon elevation
uniform vec4 vge_atmosphereDisk; // scene-linear disk radiance, angular radius
#endif
out vec3 vge_sunDirection;
out vec2 vge_sunPlane;

/** Uses the engine quad with the shared rotation-only camera projection and explicit angular size. */
void VgeDrawAtmosphericSun()
{
    vec3 sun = normalize(vge_atmosphereSun.xyz);
    vec3 right = normalize(cross(abs(sun.y) < .99 ? vec3(0, 1, 0) : vec3(0, 0, 1), sun));
    vec3 up = cross(sun, right);
    // Leave a small border for disk-edge antialiasing; the fragment radius remains exactly one.
    vge_sunPlane = (uvIn * 2.0 - 1.0) * 1.25;
    vge_sunDirection = sun + tan(vge_atmosphereDisk.w) * (right * vge_sunPlane.x + up * vge_sunPlane.y);
    // W=0 removes camera translation/bobbing. Stay just inside clear depth so the
    // engine's Less-depth occlusion query counts unobstructed solar fragments.
    gl_Position = vgeFrame.currViewProjMatrix * vec4(vge_sunDirection, 0.0);
    gl_Position.z = gl_Position.w * .999999;
}
