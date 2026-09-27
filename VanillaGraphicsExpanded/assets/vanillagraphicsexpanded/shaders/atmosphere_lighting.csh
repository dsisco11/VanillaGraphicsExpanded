#version 430
@import "./includes/atmosphere_transport.glsl"
@import "./includes/atmosphere_sky_mapping.glsl"
@import "./includes/atmosphere_solar_disk.glsl"
layout(local_size_x = 1) in;
layout(std430, binding = 2) buffer AtmosphereOutput { vec4 outputValues[]; };

// Small serial reduction preserves the CPU's row-major lighting accumulation order.
void main()
{
    int width = int(mediumSize.z), height = int(mediumSize.w);
    vec3 environment = vec3(0.0), horizon = vec3(0.0);
    for (int i = 0; i < width * height; ++i)
    {
        int y = i / width;
        vec3 radiance = outputValues[i + 4].rgb;
        environment += radiance * atmSkyEnvironmentWeight(y, width, height, work.z);
    }
    float row = atmSkyCoordinate(0.0, work.z) * float(height - 1);
    int lower = min(int(row), height - 2);
    for (int x = 0; x < width; ++x)
        horizon += mix(outputValues[4 + lower * width + x].rgb,
            outputValues[4 + (lower + 1) * width + x].rgb, row - float(lower)) / float(width);
    float elevation = asin(clamp(sunAltitude.y, -1.0, 1.0));
    float visible = atmSunVisibility(elevation, work.z);
    vec3 solar = vec3(0.0);
    if (visible > 0.0)
    {
        float sampleElevation = atmSunVisibleElevation(elevation, work.z, visible);
        solar = atmSolar * atmTransmission(vec3(0.0, atmGround + sunAltitude.w, 0.0),
            vec3(cos(sampleElevation), sin(sampleElevation), 0.0), 12) * visible;
    }
    outputValues[0] = vec4(solar, 0.0);
    outputValues[1] = vec4(environment, 0.0);
    outputValues[2] = vec4(horizon, 0.0);
    outputValues[3] = vec4(atmExtinction(atmDensity(sunAltitude.w)) * .001, 0.0);
}
