#version 430
@import "./includes/atmosphere_transport.glsl"
layout(local_size_x = 64) in;
layout(std430, binding = 1) buffer AtmosphereScattering { vec4 sources[]; };
shared vec3 angularSource[64];
shared vec3 angularFeedback[64];

// One workgroup integrates a cell, distributing angular rays across its lanes.
void main()
{
    int cell = int(work.y) + int(gl_WorkGroupID.x);
    int width = int(scatteringSize.x), height = int(scatteringSize.y);
    if (cell >= width * height) return;
    int lane = int(gl_LocalInvocationID.x), directions = int(scatteringSize.z);
    int steps = int(scatteringSize.w), lightSteps = int(work.x);
    float v = float(cell / width) / float(height - 1);
    float altitude = .001 + 99.0 * v * v;
    float cosine = 2.0 * float(cell % width) / float(width - 1) - 1.0;
    vec3 sun = vec3(sqrt(max(0.0, 1.0 - cosine * cosine)), cosine, 0.0);
    vec3 origin = vec3(0.0, atmGround + altitude, 0.0);
    vec3 source = vec3(0.0), feedback = vec3(0.0);
    for (int d = lane; d < directions; d += 64)
    {
        float up = 1.0 - 2.0 * (float(d) + .5) / float(directions);
        float azimuth = float(d) * 2.39996323;
        float horizontal = sqrt(1.0 - up * up);
        vec3 direction = vec3(horizontal * cos(azimuth), up, horizontal * sin(azimuth));
        float ground = atmGroundDistance(origin, direction);
        float distance = ground > 0.0 ? ground : atmBoundary(origin, direction);
        vec3 throughput = vec3(1.0);
        for (int i = 0; i < steps; ++i)
        {
            float start = distance * float(i * i) / float(steps * steps);
            float end = distance * float((i + 1) * (i + 1)) / float(steps * steps);
            vec3 p = origin + direction * ((start + end) * .5);
            vec3 density = atmDensity(max(0.0, length(p) - atmGround));
            vec3 extinction = atmExtinction(density);
            vec3 integral = vec3(atmSegment(extinction.x, end - start),
                atmSegment(extinction.y, end - start), atmSegment(extinction.z, end - start));
            vec3 response = throughput * atmScattering(density) * integral;
            feedback += response;
            source += response * atmTransmission(p, sun, lightSteps) / (4.0 * atmPi);
            throughput *= exp(-extinction * (end - start));
        }
        if (ground > 0.0)
        {
            vec3 normal = normalize(origin + direction * ground);
            vec3 reflected = throughput * mediumSize.y;
            source += reflected * atmTransmission(normal * (atmGround + .001), sun, lightSteps)
                * (max(0.0, dot(normal, sun)) / atmPi);
            feedback += reflected;
        }
    }
    angularSource[lane] = source;
    angularFeedback[lane] = feedback;
    barrier();
    if (lane == 0)
    {
        source = vec3(0.0); feedback = vec3(0.0);
        for (int i = 0; i < 64; ++i) { source += angularSource[i]; feedback += angularFeedback[i]; }
        source /= float(directions); feedback /= float(directions);
        sources[cell] = vec4(source / max(vec3(1e-5), vec3(1.0) - feedback), 0.0);
    }
}
