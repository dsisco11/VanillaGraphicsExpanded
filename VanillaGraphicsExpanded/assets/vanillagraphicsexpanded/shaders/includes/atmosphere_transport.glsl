#ifndef VGE_ATMOSPHERE_TRANSPORT
#define VGE_ATMOSPHERE_TRANSPORT
// Kilometres, inverse kilometres and calibrated extraterrestrial solar illuminance in lux.
const float atmGround = 6360.0, atmTop = 6460.0, atmPi = 3.14159265359;
const vec3 atmRayleigh = vec3(.005802, .013558, .0331);
const vec3 atmOzone = vec3(.000650, .001881, .000085);
const vec3 atmSolarColor = vec3(1.474, 1.8504, 1.91198);
#define atmSolar (atmSolarColor * work.w)
layout(std430, binding = 0) readonly buffer AtmosphereParameters
{
    vec4 sunAltitude;
    vec4 mediumSize; // aerosol, ground albedo, sky width, sky height
    vec4 scatteringSize; // source width, height, directions, view steps
    vec4 work; // solar steps, first source cell, geometric horizon elevation, fixed solar amplitude
};

// Positive planet intersection; a finite clear segment is not established sky visibility.
float atmGroundDistance(vec3 p, vec3 d)
{
    precise float b = dot(p, d);
    // Preserve metre-scale altitude before solving the planet-scale quadratic.
    precise float c = (p.y - atmGround) * (p.y + atmGround) + p.x * p.x + p.z * p.z;
    precise float disc = b * b - c;
    return b < 0.0 && disc >= 0.0 ? c / (-b + sqrt(disc)) : -1.0;
}

// Forward exit from the spherical atmosphere.
float atmBoundary(vec3 p, vec3 d)
{
    float b = dot(p, d);
    precise float c = (p.y - atmTop) * (p.y + atmTop) + p.x * p.x + p.z * p.z;
    float root = sqrt(max(0.0, b * b - c));
    return max(0.0, b >= 0.0 && b + root > 0.0 ? -c / (b + root) : -b + root);
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
    // A zero-distance inward surface hit also blocks sunlight; tiny tangent coordinates cannot select visibility.
    if (atmGroundDistance(p, d) >= 0.0) return vec3(0.0);
    float distance = atmBoundary(p, d);
    vec3 optical = vec3(0.0);
    for (int i = 0; i < samples; ++i)
    {
        float start = distance * float(i * i) / float(samples * samples);
        float end = distance * float((i + 1) * (i + 1)) / float(samples * samples);
        // Avoid fused planet-scale coordinates changing horizon visibility or local density.
        precise vec3 point = p + d * ((start + end) * .5);
        float h = max(0.0, length(point) - atmGround);
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
