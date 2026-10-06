#version 450 core
@import "../includes/liquids/boundary_transport.glsl"
layout(location = 0) out vec4 result;

/** Checks separated intervals and underwater initialization without assuming a single screen-depth difference. */
void main()
{
    int column = int(gl_FragCoord.x);
    int row = int(gl_FragCoord.y);
    vec3 extinction = row == 1 ? vec3(0) : vec3(.3, .5, .7);
    float lengthMetres = column == 0 ? (10.0 - 2.0) - (10.0 - 4.0)
        : column == 1 ? -(10.0 - 3.0)
        : column == 2 ? (10.0 - 2.0) - (10.0 - 4.0) + (10.0 - 6.0) - (10.0 - 9.0)
        : column == 3 ? 0.0 : -7.0;
    bool underwater = column == 1;
    vec4 optical = vec4(extinction * lengthMetres, lengthMetres);
    vec4 source = vec4(vec3(.2, .3, .4) * (underwater ? 3.0 : lengthMetres), column == 1 || column == 4 ? -1.0 : 0.0);
    if (row == 1) source.rgb = vec3(0);
    vec3 transmission;
    vec3 scattered;
    float waterLength;
    bool valid = VgeWaterBoundaryTransport(optical, source, underwater ? extinction : vec3(0),
        underwater, 10.0, transmission, scattered, waterLength);
    result = vec4(row == 2 ? scattered : transmission, valid ? waterLength : -1.0);
}
