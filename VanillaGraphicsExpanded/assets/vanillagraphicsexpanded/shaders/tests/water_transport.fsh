#version 450 core
@import "../includes/liquids/transport.glsl"
@import "../includes/atmosphere_aerial.glsl"
layout(location=0) out vec4 result;
layout(location=1) out vec4 separatelyTransported;

/** Evaluates linear confidence composition, RGB transport, and water-segment photon orientation. */
void main()
{
    int column = int(gl_FragCoord.x);
    int row = int(gl_FragCoord.y);
    float parameter = column == 0 ? 0.0 : column == 1 ? .25 : column == 2 ? .5 : 1.0;
    separatelyTransported = vec4(0);
    if (row == 0) result = VgeWaterCompose(vec3(2,4,8), .25, vec3(16,8,4), parameter);
    if (row == 1)
    {
        VgeWaterMedium medium = VgeWaterMedium(vec3(.1,.2,.3), vec3(.2,.3,.4), .7);
        result = vec4(VgeWaterTransport(medium, parameter * 10.0, vec3(8,4,2), vec3(3,4,5)), 1);
    }
    if (row == 2)
    {
        vec3 surface = vec3(.6,0,-.8);
        vec3 refracted = refract(surface, vec3(0,0,1), 1.0/1.333);
        mat3 toWorld = column < 2 ? mat3(1) : mat3(0,0,-1, 0,1,0, 1,0,0);
        vec3 outgoing = VgeWaterOutgoingDirection(surface, refracted, (column % 2) == 1, toWorld);
        vec3 sunDirection = normalize(vec3(.2,.8,-.4));
        result = vec4(outgoing, VgeWaterPhase(dot(-sunDirection, outgoing), .7));
    }
    if (row >= 3 && row < 12)
    {
        int coverage = (row - 3) / 3;
        int visibility = (row - 3) % 3;
        float alpha = coverage == 0 ? .001 : coverage == 1 ? .25 : 1.0;
        float sky = visibility == 0 ? 0.0 : visibility == 1 ? .4 : 1.0;
        vec3 displacement = vec3(0,0,-10);
        result = VgeWaterCompose(vec3(2,4,8), alpha, vec3(16,8,4), parameter);
        result.rgb = VgeApplyAerial(result.rgb, displacement, sky, vec2(0), vec3(0,1,0));
        separatelyTransported = VgeWaterCompose(
            VgeApplyAerial(vec3(2,4,8), displacement, sky, vec2(0), vec3(0,1,0)), alpha,
            VgeApplyAerial(vec3(16,8,4), displacement, sky, vec2(0), vec3(0,1,0)), parameter);
    }
    if (row == 12)
    {
        // An underwater exit transports its air segment before the water medium;
        // the shared above-water camera segment must not be applied a second time.
        vec3 air = VgeApplyAerial(vec3(8,4,2), vec3(0,0,-10), parameter, vec2(0), vec3(0,1,0));
        VgeWaterMedium medium = VgeWaterMedium(vec3(.1,.2,.3),vec3(0),0);
        result = vec4(VgeWaterTransport(medium,2.0,air,vec3(0)),1);
    }
}
