#version 330 core

@import "./includes/lumon_worldprobe.glsl"

in vec4 vColor;
in vec2 vAtlasCoord;

uniform sampler2D worldProbeDebugState0;
uniform int importanceColorMode;

out vec4 outColor;

vec4 getImportanceOrbColor(vec4 debugState, bool disabled, bool unavailable)
{
    float importance = clamp(debugState.a, 0.0, 1.0);
    vec3 color = mix(vec3(0.05, 0.25, 1.0), vec3(1.0, 0.12, 0.02), importance);

    if (disabled)
    {
        return vec4(1.0, 0.0, 1.0, 1.0);
    }
    else if (unavailable)
    {
        return vec4(1.0, 0.65, 0.0, 1.0);
    }

    return vec4(color, 1.0);
}

void main(void)
{
    // Orb impostor in point sprite space.
    vec2 p = gl_PointCoord * 2.0 - 1.0;
    // gl_PointCoord has its origin at the lower-left; flip so +Y is "up" on screen.
    p.y = -p.y;
    float r2 = dot(p, p);
    if (r2 > 1.0) discard;

    float z = sqrt(max(1.0 - r2, 0.0));

    // Convert view-facing normal into world-space direction.
    // Use explicit matrix-vector multiplies to avoid row/column-major confusion.
    // Note: view-space +Z points toward the camera (OpenGL camera looks down -Z).
    vec3 rightWS = normalize((invViewMatrix * vec4(1.0, 0.0, 0.0, 0.0)).xyz);
    vec3 upWS = normalize((invViewMatrix * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
    vec3 viewZWS = normalize((invViewMatrix * vec4(0.0, 0.0, 1.0, 0.0)).xyz);

    // Camera-facing sphere normal in world space.
    vec3 N = normalize(rightWS * p.x + upWS * p.y + viewZWS * z);

    // Approximate view direction from the fragment toward the camera.
    // (The sphere is a camera-facing impostor; using the camera forward axis is stable and fast.)
    vec3 V = normalize(viewZWS);
    float NoV = clamp(dot(N, V), 0.0, 1.0);
    vec3 R = normalize(reflect(-V, N));

    ivec2 ac = ivec2(floor(vAtlasCoord + vec2(0.5)));

    // Debug lifecycle state (uploaded by CPU). Disabled probes receive a magenta
    // marker and unavailable probes receive an amber marker.
    vec4 dbg = texelFetch(worldProbeDebugState0, ac, 0);
    bool disabled = (dbg.r > 0.5) && (dbg.b > 0.5) && (dbg.g < 0.5);
    bool unavailable = (dbg.r > 0.5) && (dbg.g > 0.5) && (dbg.b < 0.5);

    if (importanceColorMode != 0)
    {
        outColor = getImportanceOrbColor(dbg, disabled, unavailable);
        return;
    }

    // Decode storage index + level from the scalar atlas coordinate.
    int resolution = VGE_LUMON_WORLDPROBE_RESOLUTION;
    int u = ac.x;
    int v = ac.y;
    int storageX = (resolution > 0) ? (u % resolution) : 0;
    int storageZ = (resolution > 0) ? (u / resolution) : 0;
    int storageY = (resolution > 0) ? (v % resolution) : 0;
    int level = (resolution > 0) ? (v / resolution) : 0;
    ivec3 storage = ivec3(storageX, storageY, storageZ);

    float skyIntensity = clamp(texelFetch(worldProbeVis0, ac, 0).z, 0.0, 1.0);

    // Envmap-style visualization: sample directional radiance from the octahedral tile.
    ivec2 octTexel = lumonWorldProbeDirectionToOctahedralTexel(R);
    vec4 t = lumonWorldProbeFetchRadianceAtlasTexel(
        worldProbeRadianceAtlas,
        storage,
        level,
        resolution,
        octTexel);

    vec3 refl;
    if (lumonWorldProbeIsSkyVisible(t.a))
    {
        refl = max(lumonWorldProbeGetSkyTint(), vec3(0.0)) * skyIntensity;
    }
    else
    {
        refl = max(t.rgb, vec3(0.0));
    }

    // Simple Fresnel to make the orb read as a reflective sphere.
    float F0 = 0.04;
    float fresnel = F0 + (1.0 - F0) * pow(1.0 - NoV, 5.0);

    vec3 col = refl * (0.25 + 0.75 * fresnel);

    // Slight level tint + minimum visibility.
    col = max(col, vec3(0.04));
    col *= mix(vec3(1.0), vColor.rgb, 0.20);

    if (disabled)
    {
        float markerRadius = 0.55;
        float marker = 1.0 - smoothstep(markerRadius * markerRadius * 0.7, markerRadius * markerRadius, r2);
        col = mix(col, vec3(1.0, 0.0, 1.0), marker);
    }
    else if (unavailable)
    {
        float markerRadius = 0.55;
        float marker = 1.0 - smoothstep(markerRadius * markerRadius * 0.7, markerRadius * markerRadius, r2);
        col = mix(col, vec3(1.0, 0.65, 0.0), marker);
    }

    outColor = vec4(col, 1.0);
}
