uniform int vge_atmosphereSunDraw;
uniform vec4 vge_atmosphereSun;
uniform vec4 vge_atmosphereDisk;
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
    outColor = vec4(VgeDitherDisplay(displayColor, gl_FragCoord.xy), coverage);
    // Feed the engine's bloom extraction separately from atmospheric scattering.
    // Use attenuated, underwater-adjusted color before dithering, so a dim disk
    // fades its bloom too. Coverage is applied by the existing attachment blending.
    float bloom = clamp(max(displayColor.r, max(displayColor.g, displayColor.b)), 0.0, 1.0);
    outGlow = vec4(bloom, extraGodray, 0.0, coverage);
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
