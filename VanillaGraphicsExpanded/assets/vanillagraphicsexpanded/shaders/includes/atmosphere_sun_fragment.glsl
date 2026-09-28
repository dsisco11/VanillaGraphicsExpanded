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
    // No second authored halo: atmospheric scattering provides it. Retain the existing godray channel.
    outGlow = vec4(0.0, extraGodray, 0.0, coverage);
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
