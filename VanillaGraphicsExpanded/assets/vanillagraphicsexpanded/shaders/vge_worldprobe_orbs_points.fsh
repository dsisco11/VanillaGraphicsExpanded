#version 330 core

@import "./includes/lumon_worldprobe.glsl"

in vec4 vColor;
in vec2 vAtlasCoord;

uniform mat4 invViewMatrix;
uniform sampler2D worldProbeDebugState0;

out vec4 outColor;

vec3 tonemap(vec3 hdr)
{
    hdr = max(hdr, vec3(0.0));
    return hdr / (hdr + vec3(1.0));
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

    // Debug lifecycle state (uploaded by CPU). Disabled probes are shown with a red center marker
    // so "no data" is visually distinct from "valid but dark".
    vec4 dbg = texelFetch(worldProbeDebugState0, ac, 0);
    bool disabled = (dbg.r > 0.5) && (dbg.b > 0.5) && (dbg.g < 0.5);

    vec4 t0 = texelFetch(worldProbeSH0, ac, 0);
    vec4 t1 = texelFetch(worldProbeSH1, ac, 0);
    vec4 t2 = texelFetch(worldProbeSH2, ac, 0);

    vec4 shR, shG, shB;
    lumonWorldProbeDecodeShL1(t0, t1, t2, shR, shG, shB);

    // Envmap-style visualization (specular-like): evaluate SH in the reflection direction.
    // Note: the world-probe cache stores low-order SH (L1), so this is intentionally low-frequency.
    vec3 reflBlock = shEvaluateRGB(shR, shG, shB, R);

    vec4 shSky = texelFetch(worldProbeSky0, ac, 0);
    float skyIntensity = clamp(texelFetch(worldProbeVis0, ac, 0).z, 0.0, 1.0);
    float reflSky = shEvaluate(shSky, R) * skyIntensity;

    vec3 refl = max(reflBlock, vec3(0.0)) + max(reflSky, 0.0) * max(worldProbeSkyTint, vec3(0.0));

    // Simple Fresnel to make the orb read as a reflective sphere.
    float F0 = 0.04;
    float fresnel = F0 + (1.0 - F0) * pow(1.0 - NoV, 5.0);

    vec3 col = tonemap(refl) * (0.25 + 0.75 * fresnel);

    // Slight level tint + minimum visibility.
    col = max(col, vec3(0.04));
    col *= mix(vec3(1.0), vColor.rgb, 0.20);

    if (disabled)
    {
        float dotR = 0.35;
        float dotMask = 1.0 - smoothstep(dotR * dotR * 0.65, dotR * dotR, r2);
        col = mix(col, vec3(1.0, 0.0, 0.0), dotMask);
    }

    outColor = vec4(col, 1.0);
}
