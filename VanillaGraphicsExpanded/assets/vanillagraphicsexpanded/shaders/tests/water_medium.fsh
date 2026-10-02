#version 450 core
@import "../includes/liquids/medium.glsl"
layout(location = 0) out vec4 result;

/** Emits independent analytic cases for the exact production medium evaluation. */
void main()
{
    int column = int(gl_FragCoord.x);
    int row = int(gl_FragCoord.y);
    float distanceMetres = column == 0 ? 0.0 : column == 1 ? .000001 : column == 2 ? 1.0 : column == 3 ? 10.0 : 100.0;
    VgeWaterMedium medium = VgeWaterMedium(vec3(.340, .0565, .00922), vec3(0), 0.0);
    if (row == 1) medium = VgeWaterMedium(vec3(.1, .2, .3), vec3(.2, .3, .4), 0.0);
    if (row == 2) medium = VgeWaterMedium(vec3(0), vec3(0), 0.0);
    vec3 transmission = VgeWaterTransmittance(medium, distanceMetres);
    vec3 source = row == 3 ? vec3(0) : vec3(2, 3, 4);
    if (row == 3) medium.scattering = vec3(.2);
    vec3 scattered = VgeWaterInScattering(medium, distanceMetres, source);
    result = vec4(row == 1 || row == 3 ? scattered : transmission,
        VgeWaterPhase(column == 0 ? 1.0 : -1.0, .7));
}
