#version 430
@import "./includes/atmosphere_transport.glsl"
@import "./includes/atmosphere_sky_mapping.glsl"
layout(local_size_x = 64) in;
layout(std430, binding = 1) readonly buffer AtmosphereScattering { vec4 sources[]; };
layout(std430, binding = 2) buffer AtmosphereOutput { vec4 outputValues[]; };

// Manual bilinear interpolation preserves the CPU table's squared-altitude coordinates.
vec3 atmSampleSource(float altitude, float cosine)
{
    int width = int(scatteringSize.x), height = int(scatteringSize.y);
    float x = (clamp(cosine, -1.0, 1.0) + 1.0) * .5 * float(width - 1);
    float y = sqrt(clamp((altitude - .001) / 99.0, 0.0, 1.0)) * float(height - 1);
    int ix = min(int(x), width - 2), iy = min(int(y), height - 2);
    return mix(mix(sources[iy * width + ix].rgb, sources[iy * width + ix + 1].rgb, x - float(ix)),
        mix(sources[(iy + 1) * width + ix].rgb, sources[(iy + 1) * width + ix + 1].rgb, x - float(ix)), y - float(iy));
}

// Each invocation evaluates one sky direction including isotropic multiple scattering.
void main()
{
    int index = int(gl_GlobalInvocationID.x), width = int(mediumSize.z), height = int(mediumSize.w);
    if (index >= width * height) return;
    float elevation = atmSkyElevation(float(index / width) / float(height - 1), work.z);
    float azimuth = (float(index % width) + .5) / float(width) * (2.0 * atmPi);
    vec3 direction = normalize(vec3(cos(elevation) * cos(azimuth), sin(elevation), cos(elevation) * sin(azimuth)));
    vec3 sun = sunAltitude.xyz, origin = vec3(0.0, atmGround + sunAltitude.w, 0.0);
    float distance = atmBoundary(origin, direction), ground = atmGroundDistance(origin, direction);
    if (ground > 0.0) distance = min(distance, ground);
    float cosine = clamp(dot(direction, sun), -1.0, 1.0);
    float rayleighPhase = 3.0 * (1.0 + cosine * cosine) / (16.0 * atmPi);
    const float g = .76;
    float miePhase = (1.0 - g * g) / (4.0 * atmPi * pow(1.0 + g * g - 2.0 * g * cosine, 1.5));
    vec3 optical = vec3(0.0), radiance = vec3(0.0);
    for (int i = 0; i < 24; ++i)
    {
        float start = distance * float(i * i) / 576.0;
        float end = distance * float((i + 1) * (i + 1)) / 576.0;
        vec3 p = origin + direction * ((start + end) * .5);
        float radius = length(p);
        vec3 density = atmDensity(max(0.0, radius - atmGround));
        vec3 extinction = atmExtinction(density);
        vec3 view = exp(-(optical + extinction * (.5 * (end - start))));
        vec3 scattering = atmRayleigh * (density.x * rayleighPhase) + vec3(.003996 * mediumSize.x * density.y * miePhase);
        radiance += view * atmTransmission(p, sun, 12) * scattering * (end - start);
        radiance += view * atmScattering(density) * atmSampleSource(radius - atmGround, dot(p, sun) / radius) * (end - start);
        optical += extinction * (end - start);
    }
    outputValues[index + 4] = vec4(max(vec3(0.0), radiance * atmSolar), 1.0);
}
