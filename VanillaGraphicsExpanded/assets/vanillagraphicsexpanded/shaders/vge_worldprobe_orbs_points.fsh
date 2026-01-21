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

    vec3 N = normalize(rightWS * p.x + upWS * p.y + viewZWS * z);

    ivec2 ac = ivec2(floor(vAtlasCoord + vec2(0.5)));
    vec4 t0 = texelFetch(worldProbeSH0, ac, 0);
    vec4 t1 = texelFetch(worldProbeSH1, ac, 0);
    vec4 t2 = texelFetch(worldProbeSH2, ac, 0);

    vec4 shR, shG, shB;
    lumonWorldProbeDecodeShL1(t0, t1, t2, shR, shG, shB);

    vec3 irr = shEvaluateDiffuseRGB(shR, shG, shB, N);
    vec3 col = tonemap(irr);

    // Slight level tint + minimum visibility.
    col = max(col, vec3(0.04));
    col *= mix(vec3(1.0), vColor.rgb, 0.25);

    outColor = vec4(col, 1.0);
}
