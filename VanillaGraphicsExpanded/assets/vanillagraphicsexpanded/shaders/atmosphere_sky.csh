#version 430
@import "./includes/atmosphere_transport.glsl"
@import "./includes/atmosphere_sky_mapping.glsl"
@import "./includes/atmosphere_aerial_mapping.glsl"
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
    vec3 optical = vec3(0.0), radiance = vec3(0.0), mieTransport = vec3(0.0);
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
        vec3 sunlight = atmTransmission(p, sun, 12);
        radiance += view * sunlight * scattering * (end - start);
        mieTransport += view * sunlight * (.003996 * mediumSize.x * density.y) * (end - start);
        radiance += view * atmScattering(density) * atmSampleSource(radius - atmGround, dot(p, sun) / radius) * (end - start);
        optical += extinction * (end - start);
    }
    outputValues[index + 4] = vec4(max(vec3(0.0), radiance * atmSolar), 1.0);
    // Cumulative finite paths share the admitted medium/source table. Each interval
    // integrates two locally constant samples analytically, with fixed work per ray.
    int count = width * height, base = 4 + count, transBase = base + count * vgeAerialDepth;
    int mieBase = transBase + count * vgeAerialDepth, aerialMieBase = mieBase + count;
    outputValues[mieBase + index] = vec4(mieTransport * atmSolar, 1.0);
    outputValues[aerialMieBase + index] = vec4(0.0, 0.0, 0.0, 1.0);
    vec3 throughput = vec3(1.0), aerial = vec3(0.0), aerialMie = vec3(0.0);
    outputValues[base + index] = vec4(0.0, 0.0, 0.0, 1.0);
    outputValues[transBase + index] = vec4(0.0, 0.0, 0.0, 1.0);
    float previous = 0.0;
    for (int slice = 1; slice < vgeAerialDepth; ++slice)
    {
        float end = vgeAerialDistance(slice, distance), step = (end - previous) * .5;
        for (int sampleIndex = 0; sampleIndex < 2 && step > 0.0; ++sampleIndex)
        {
            vec3 p = origin + direction * (previous + (float(sampleIndex) + .5) * step);
            float radius = length(p);
            vec3 density = atmDensity(max(0.0, radius - atmGround));
            vec3 extinction = atmExtinction(density);
            vec3 sunlight = atmTransmission(p, sun, 12);
            vec3 source = sunlight
                * (atmRayleigh * (density.x * rayleighPhase) + vec3(.003996 * mediumSize.x * density.y * miePhase));
            source += atmScattering(density) * atmSampleSource(radius - atmGround, dot(p, sun) / radius);
            vec3 integral = vec3(atmSegment(extinction.x, step), atmSegment(extinction.y, step), atmSegment(extinction.z, step));
            aerial += throughput * source * integral;
            aerialMie += throughput * sunlight * (.003996 * mediumSize.x * density.y) * integral;
            throughput *= exp(-extinction * step);
        }
        outputValues[base + slice * count + index] = vec4(max(vec3(0.0), aerial * atmSolar), 1.0);
        outputValues[aerialMieBase + slice * count + index] = vec4(aerialMie * atmSolar, 1.0);
        outputValues[transBase + slice * count + index] = vec4(vec3(1.0) - throughput, 1.0);
        previous = end;
    }
    // Align terminal source quadrature with the sky; extinction remains the finite-path integral.
    // Normalize smooth terms independently, avoiding a baked angular factor in the correction.
    vec3 scale = max(vec3(0.0), radiance - mieTransport * miePhase)
        / max(aerial - aerialMie * miePhase, vec3(1e-20));
    vec3 mieScale = mieTransport / max(aerialMie, vec3(1e-20));
    for (int slice = 1; slice < vgeAerialDepth; ++slice)
    {
        int address = base + slice * count + index, mieAddress = aerialMieBase + slice * count + index;
        vec3 rawMie = outputValues[mieAddress].rgb;
        outputValues[mieAddress].rgb = rawMie * mieScale;
        outputValues[address].rgb = max(vec3(0.0), outputValues[address].rgb - rawMie * miePhase) * scale
            + outputValues[mieAddress].rgb * miePhase;
    }
}
