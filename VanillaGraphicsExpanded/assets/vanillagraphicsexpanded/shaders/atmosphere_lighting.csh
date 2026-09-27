#version 430
@import "./includes/atmosphere_transport.glsl"
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
        float elevation = ((float(y) + .5) / float(height) - .5) * atmPi;
        float up = sin(elevation);
        vec3 radiance = outputValues[i + 4].rgb;
        if (up > 0.0) environment += radiance * (up * cos(elevation) * 2.0 * atmPi / float(width * height));
        if (y == height / 2) horizon += radiance / float(width);
    }
    outputValues[0] = vec4(atmSolar * atmTransmission(vec3(0.0, atmGround + sunAltitude.w, 0.0), sunAltitude.xyz, 12), 0.0);
    outputValues[1] = vec4(environment, 0.0);
    outputValues[2] = vec4(horizon, 0.0);
    outputValues[3] = vec4(atmExtinction(atmDensity(sunAltitude.w)) * .001, 0.0);
}
