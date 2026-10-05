#version 450 core
@import "../includes/liquids/transport.glsl"
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
    vec3 source = row == 3 ? vec3(0) : vec3(2, 3, 4);
    if (row == 3) medium.scattering = vec3(.2);
    VgeWaterPath path = VgeWaterEvaluatePath(medium, distanceMetres);
    vec3 transmission = row == 0 || row == 2
        ? VgeWaterTransmittance(medium, distanceMetres) : path.transmittance;
    vec3 scattered = VgeWaterInScattering(medium, path, source);
    result = vec4(row == 1 || row == 3 ? scattered : transmission,
        VgeWaterPhase(column == 0 ? 1.0 : -1.0, .7));
    if (row >= 4)
    {
        medium = VgeWaterMedium(vec3(.1,.2,.3),vec3(.2,.3,.4),0);
        // Defensive zero-extinction limit; authored media never use negative absorption.
        if (row == 4) medium.absorption = -medium.scattering;
        if (row == 5) distanceMetres = -distanceMetres;
        if (row == 7) medium.scattering = vec3(0);
        if (row == 8)
        {
            // Defensive invalid-authoring inputs exercise source and coefficient clamps independently.
            medium.scattering = vec3(.2,.3,-.4);
            source = vec3(-2,3,4);
        }
        if (row == 9)
        {
            medium = VgeWaterMedium(vec3(.5),vec3(.5),0);
            distanceMetres = column == 0 ? 0.0 : column == 1 ? .0009999
                : column == 2 ? .001 : column == 3 ? .0010001 : 100.0;
        }
        result = vec4(VgeWaterTransport(medium,distanceMetres,vec3(8,4,2),source),1);
    }
    if (row >= 10)
    {
        float values[7] = float[7](0.0,.7,-.7,.0000001,-.0000001,2.0,-2.0);
        float cosine = float(column) * .5 - 1.0;
        vec3 incoming = vec3(sqrt(max(0.0,1.0-cosine*cosine)),0,cosine);
        result = vec4(VgeWaterPhase(cosine,values[row-10]),
            VgeWaterPhase(incoming,vec3(0,0,1),values[row-10]),0,1);
    }
}
