#version 330 core

@import "./includes/lumon_worldprobe.glsl"

in vec4 vColor;
in vec2 vAtlasCoord;

uniform mat4 invViewMatrix;

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

    // For probe visualization, sample irradiance using a stable world-space normal.
    // Using the per-fragment sphere normal makes the underside of probes (near the ground) look black
    // even when the probe's overall lighting is reasonable.
    vec3 Nsample = N; //vec3(0.0, 1.0, 0.0);

    ivec2 ac = ivec2(floor(vAtlasCoord + vec2(0.5)));
    vec4 t0 = texelFetch(worldProbeSH0, ac, 0);
    vec4 t1 = texelFetch(worldProbeSH1, ac, 0);
    vec4 t2 = texelFetch(worldProbeSH2, ac, 0);

    vec4 shR, shG, shB;
    lumonWorldProbeDecodeShL1(t0, t1, t2, shR, shG, shB);

    // Display hemisphere-integrated irradiance (cosine-weighted), not Lambert outgoing radiance.
    // This avoids negative-lobe artifacts from direct SH evaluation and better matches "how much light hits"
    // a surface with normal N.
    vec3 irrBlock = shEvaluateHemisphereIrradianceRGB(shR, shG, shB, Nsample);

    vec4 shSky = texelFetch(worldProbeSky0, ac, 0);
    float irrSky = shEvaluateHemisphereIrradiance(shSky, Nsample);

    // Debug visualization: treat the sky-intensity channel as "how much sky/sun reaches here".
    // Use a neutral, bright tint so probes near the ground don't appear black simply because
    // the engine's ambient tint is dark.
    vec3 skyVizTint = vec3(1.0);
    vec3 irr = max(irrBlock, vec3(0.0)) + skyVizTint * max(irrSky, 0.0);
    vec3 col = tonemap(irr);

    // Apply a gentle shading term so the point sprite still reads as a sphere.
    float shade = 0.75 + 0.25 * clamp(z, 0.0, 1.0);
    col *= shade;

    // Slight level tint + minimum visibility.
    col = max(col, vec3(0.04));
    col *= mix(vec3(1.0), vColor.rgb, 0.25);

    outColor = vec4(col, 1.0);
}
