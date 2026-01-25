// Shared helpers for LumOn debug shader entrypoints.

vec3 heatmap(float t)
{
    // Blue -> Cyan -> Green -> Yellow -> Red
    t = clamp(t, 0.0, 1.0);
    vec3 c;
    if (t < 0.25)
    {
        c = mix(vec3(0.0, 0.0, 1.0), vec3(0.0, 1.0, 1.0), t * 4.0);
    }
    else if (t < 0.5)
    {
        c = mix(vec3(0.0, 1.0, 1.0), vec3(0.0, 1.0, 0.0), (t - 0.25) * 4.0);
    }
    else if (t < 0.75)
    {
        c = mix(vec3(0.0, 1.0, 0.0), vec3(1.0, 1.0, 0.0), (t - 0.5) * 4.0);
    }
    else
    {
        c = mix(vec3(1.0, 1.0, 0.0), vec3(1.0, 0.0, 0.0), (t - 0.75) * 4.0);
    }
    return c;
}

vec3 vgeTonemapReinhard(vec3 c)
{
    c = max(c, vec3(0.0));
    return c / (c + vec3(1.0));
}

mat4 getViewMatrix()
{
    return inverse(invViewMatrix);
}

vec3 worldToViewPos(vec3 posWS)
{
    return (getViewMatrix() * vec4(posWS, 1.0)).xyz;
}
