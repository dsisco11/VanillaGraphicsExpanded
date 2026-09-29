#version 330 core

out vec4 outColor;
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/vge_tonemap_reinhard.glsl"

/** Displays only the actual deferred sunlight transmission, tone mapped against black. */
void main()
{
    vec2 uv = gl_FragCoord.xy / screenSize;
    vec3 transmission = vec3(texture(directDiffuse, uv).a,
        texture(directSpecular, uv).a, texture(emissive, uv).a);
    outColor = vec4(vgeTonemapReinhard(transmission), 1.0);
}
