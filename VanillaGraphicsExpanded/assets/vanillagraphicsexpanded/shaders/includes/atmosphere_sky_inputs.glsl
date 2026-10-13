@import "./vge_frame_ubo.glsl"
layout(std140, binding = 28) uniform SkyInputs {
    vec4 skySunHorizon;
    vec4 skyFog;
    vec4 skyDayWeather;
    vec4 skyMurk;
    vec4 skyEffects;
    vec4 skyEncoding; // inverse immutable lookup scale; RGB only
    vec4 skyMoon;
};
