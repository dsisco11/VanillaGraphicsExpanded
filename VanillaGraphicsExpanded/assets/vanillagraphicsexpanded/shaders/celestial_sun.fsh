#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/vge_frame_ubo.glsl"
@import "./includes/pbr_frame_lighting.glsl"
layout(std140, binding = 28) uniform SunInputs
{
    vec4 sunCentreRadius;
    vec4 sunRightHorizon;
    vec4 sunUpStorage;
    vec4 sunRadiance;
};
layout(location = 0) out vec4 outColor;
layout(location = 1) out vec4 outGlow;

/** Adds the atmosphere-calibrated finite disk with pixel coverage and solar visibility metadata. */
void main()
{
    vec2 ndc = gl_FragCoord.xy / vgeFrame.screenSize * 2.0 - 1.0;
    // Form the ray in view space before rotation. Subtracting translated world
    // positions would lose limb precision as the camera moves far from the origin.
    vec4 a = vgeFrame.invProjectionMatrix * vec4(ndc, -1, 1);
    vec4 b = vgeFrame.invProjectionMatrix * vec4(ndc, 0, 1);
    vec3 direction = normalize(mat3(vgeFrame.invViewMatrix) * (b.xyz / b.w - a.xyz / a.w));
    vec2 disk = vec2(dot(direction, sunRightHorizon.xyz), dot(direction, sunUpStorage.xyz)) / sin(sunCentreRadius.w);
    float radius2 = dot(disk, disk);
    float edge = max(fwidth(radius2), 1e-6);
    float elevation = asin(clamp(direction.y, -1.0, 1.0));
    float horizonEdge = max(fwidth(elevation), 1e-6);
    float coverage = (1.0 - smoothstep(1.0 - edge * .5, 1.0 + edge * .5, radius2))
        * smoothstep(sunRightHorizon.w - horizonEdge * .5, sunRightHorizon.w + horizonEdge * .5, elevation);
    if (lightingStorage.w < .5 || dot(direction, sunCentreRadius.xyz) <= 0.0 || coverage <= 0.0
        || max(sunRadiance.r, max(sunRadiance.g, sunRadiance.b)) <= 0.0) discard;
    // Atmospheric extinction is already present in the published disk radiance.
    // Select the CPU limit before multiplication; scene illumination keeps its original calibration.
    vec3 stored;
    if (!VgeStoreVisibleSolarDisk(sunRadiance.rgb, sunUpStorage.w, stored)) discard;
    outColor = vec4(stored * coverage, 0.0);
    // Bloom extracts the HDR color; green alone marks the solar disk for shaft visibility.
    outGlow = vec4(0.0, coverage, 0.0, 0.0);
}
