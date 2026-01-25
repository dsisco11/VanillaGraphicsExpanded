// Debug Mode 22-25: Direct Lighting Debug Views (Phase 16)

vec4 renderDirectDiffuseDebug()
{
    return vec4(vgeTonemapReinhard(texture(directDiffuse, uv).rgb), 1.0);
}

vec4 renderDirectSpecularDebug()
{
    return vec4(vgeTonemapReinhard(texture(directSpecular, uv).rgb), 1.0);
}

vec4 renderDirectEmissiveDebug()
{
    return vec4(vgeTonemapReinhard(texture(emissive, uv).rgb), 1.0);
}

vec4 renderDirectTotalDebug()
{
    vec3 dd = texture(directDiffuse, uv).rgb;
    vec3 ds = texture(directSpecular, uv).rgb;
    return vec4(vgeTonemapReinhard(dd + ds), 1.0);
}

// Program entry: Direct
vec4 RenderDebug_Direct(vec2 screenPos)
{
    switch (debugMode)
    {
        case 22: return renderDirectDiffuseDebug();
        case 23: return renderDirectSpecularDebug();
        case 24: return renderDirectEmissiveDebug();
        case 25: return renderDirectTotalDebug();
        default: return vec4(0.0, 0.0, 0.0, 1.0);
    }
}
