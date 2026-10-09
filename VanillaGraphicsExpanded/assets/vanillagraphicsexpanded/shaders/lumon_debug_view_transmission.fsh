#version 330 core

out vec4 outColor;
@import "./includes/lumon_debug_uniforms.glsl"
@import "./includes/debug/vge_tonemap_reinhard.glsl"

/** Displays only the actual deferred sunlight transmission, tone mapped against black. */
void main()
{
    vec2 uv = gl_FragCoord.xy / vgeFrame.screenSize;
    vec3 transmission = vec3(texture(directLighting, vec3(uv, VGE_DIRECT_DIFFUSE)).a,
        texture(directLighting, vec3(uv, VGE_DIRECT_SPECULAR)).a, texture(directLighting, vec3(uv, VGE_DIRECT_EMISSIVE)).a);
    outColor = vec4(vgeTonemapReinhard(transmission), 1.0);
}
