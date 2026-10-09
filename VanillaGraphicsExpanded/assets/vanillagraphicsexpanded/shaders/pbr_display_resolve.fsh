#version 330 core
#extension GL_ARB_shading_language_420pack : require
@import "./includes/pbr_color.glsl"
@import "./includes/lumon_common.glsl"

uniform sampler2D primaryScene;
uniform sampler2D primaryDepth;
uniform sampler2D particleLayer;
layout(std140, binding = 28) uniform DisplayResolveInputs
{
    int sceneLinear;
    int particleLayerEnabled;
};
layout(location = 0) out vec4 outColor;

/** Hands off linear scene radiance or converts deferred geometry on the compatible legacy route. */
void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    vec4 scene = texelFetch(primaryScene, pixel, 0);
    if (sceneLinear != 0 && particleLayerEnabled != 0)
    {
        // Particle RGB already includes coverage and legacy fog; only the
        // background receives transmission. Publication guarantees matching dimensions.
        vec4 particles = texelFetch(particleLayer, pixel, 0);
        scene.rgb = scene.rgb * (1.0 - clamp(particles.a, 0.0, 1.0)) + particles.rgb;
    }
    float depth = texelFetch(primaryDepth, pixel, 0).r;
    outColor = sceneLinear != 0 || lumonIsSky(depth) ? scene
        : vec4(VgeDitherDisplay(VgeResolveDisplay(scene.rgb), gl_FragCoord.xy), scene.a);
}
