#ifndef VGE_ATMOSPHERE_TRANSPORT
#define VGE_ATMOSPHERE_TRANSPORT
// Kilometres, inverse kilometres and relative extraterrestrial solar irradiance.
const float atmGround = 6360.0, atmTop = 6460.0, atmPi = 3.14159265359;
const vec3 atmRayleigh = vec3(.005802, .013558, .0331);
const vec3 atmOzone = vec3(.000650, .001881, .000085);
const vec3 atmSolar = vec3(1.474, 1.8504, 1.91198);
layout(std430, binding = 0) readonly buffer AtmosphereParameters
{
    vec4 sunAltitude;
    vec4 mediumSize; // aerosol, ground albedo, sky width, sky height
    vec4 scatteringSize; // source width, height, directions, view steps
    vec4 work; // solar steps, first source cell, unused, unused
};

// Positive planet intersection; a finite clear segment is not established sky visibility.
float atmGroundDistance(vec3 p, vec3 d)
{
    float b = dot(p, d);
    float disc = b * b - (dot(p, p) - atmGround * atmGround);
    return b < 0.0 && disc >= 0.0 ? -b - sqrt(disc) : -1.0;
}

// Forward exit from the spherical atmosphere.
float atmBoundary(vec3 p, vec3 d)
{
    float b = dot(p, d);
    return max(0.0, -b + sqrt(max(0.0, b * b - (dot(p, p) - atmTop * atmTop))));
}

// Molecular/aerosol exponential profiles and the triangular ozone layer.
vec3 atmDensity(float h)
{
    return vec3(exp(-h / 8.0), exp(-h / 1.2), max(0.0, 1.0 - abs(h - 25.0) / 15.0));
}

// Total extinction includes aerosol absorption; scattering alone excludes it.
vec3 atmExtinction(vec3 density)
{
    return atmRayleigh * density.x + vec3(.004440 * mediumSize.x * density.y) + atmOzone * density.z;
}

// The radiative source excludes aerosol absorption from the total extinction.
vec3 atmScattering(vec3 density)
{
    return atmRayleigh * density.x + vec3(.003996 * mediumSize.x * density.y);
}

// Match the CPU reference's quadratic solar integration and planet shadow.
vec3 atmTransmission(vec3 p, vec3 d, int samples)
{
    if (atmGroundDistance(p, d) > 0.0) return vec3(0.0);
    float distance = atmBoundary(p, d);
    vec3 optical = vec3(0.0);
    for (int i = 0; i < samples; ++i)
    {
        float start = distance * float(i * i) / float(samples * samples);
        float end = distance * float((i + 1) * (i + 1)) / float(samples * samples);
        float h = max(0.0, length(p + d * ((start + end) * .5)) - atmGround);
        optical += atmExtinction(atmDensity(h)) * (end - start);
    }
    return exp(-optical);
}

// Stable analytic integral for thin and optically thick segments.
float atmSegment(float extinction, float distance)
{
    float optical = extinction * distance;
    return optical < .001 ? distance * (1.0 - optical * .5 + optical * optical / 6.0)
        : (1.0 - exp(-optical)) / extinction;
}
#endif
