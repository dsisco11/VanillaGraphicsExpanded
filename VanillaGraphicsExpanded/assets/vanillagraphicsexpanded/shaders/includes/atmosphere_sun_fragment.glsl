#ifndef VGE_ATMOSPHERE_SUN_INPUTS
uniform int vge_atmosphereSunDraw;
uniform vec4 vge_atmosphereSun;
uniform vec4 vge_atmosphereDisk;
#endif
in vec3 vge_sunDirection;
in vec2 vge_sunPlane;

// Visible-disk radiance already includes atmospheric extinction; never multiply by vanilla tint/fog.
void VgeDrawAtmosphericSun()
{
    float radius = length(vge_sunPlane);
    float elevation = asin(clamp(normalize(vge_sunDirection).y, -1.0, 1.0));
    float edge = max(fwidth(radius), .00001);
    float horizonEdge = max(fwidth(elevation), .000001);
    float coverage = (1.0 - smoothstep(1.0 - edge * .5, 1.0 + edge * .5, radius))
        * smoothstep(vge_atmosphereSun.w - horizonEdge * .5, vge_atmosphereSun.w + horizonEdge * .5, elevation);
    if (coverage <= 0.0 || max(max(vge_atmosphereDisk.r, vge_atmosphereDisk.g), vge_atmosphereDisk.b) <= 0.0) discard;
    vec3 displayColor = applyUnderwaterEffects(VgeResolveDisplay(vge_atmosphereDisk.rgb), getSkyMurkiness());
    // HDR keeps the disk's physical radiance. Its water transport is owned by scene
    // composition; the legacy display tint remains only on the compatible SDR route.
    outColor = vec4(vge_pbrRoute != 0 ? max(vge_atmosphereDisk.rgb, vec3(0.0))
        : VgeDitherDisplay(displayColor, gl_FragCoord.xy), coverage);
    // Owned HDR bloom extracts actual radiance. Green is solar visibility only;
    // attachment blending carries coverage/occlusion to the owned shaft pass.
    // Display-referred offscreen draws retain the engine metadata convention.
    float bloom = clamp(max(displayColor.r, max(displayColor.g, displayColor.b)), 0.0, 1.0);
    outGlow = vec4(vge_pbrRoute != 0 ? 0.0 : bloom, vge_pbrRoute != 0 ? 1.0 : extraGodray, 0.0, coverage);
    #if SSAOLEVEL > 0
    outGPosition = vec4(0.0, 0.0, 0.0, 1.0);
    outGNormal = vec4(0.0);
    #endif
    #if VGE_SURFACE_PRIMARY_OUTPUTS
    vge_outNormal = vec4(0.0);
    vge_outMaterial = vec4(0.0);
    vge_outPatchId = uvec4(0u);
    vge_outEnvironment = vec4(0.0);
    #endif
    #if defined(ALLOWDEPTHOFFSET) && ALLOWDEPTHOFFSET > 0
    gl_FragDepth = gl_FragCoord.z;
    #endif
}
