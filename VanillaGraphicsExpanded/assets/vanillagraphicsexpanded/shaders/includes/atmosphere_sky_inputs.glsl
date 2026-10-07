layout(std140) uniform SkyInputs {
    mat4 skyInverseViewProjection;
    mat4 skyViewProjection;
    vec4 skySunHorizon;
    vec4 skyFog;
    vec4 skyDayWeather;
    vec4 skyDepthFrame;
    vec4 skyMurk;
    vec4 skyEffects;
};
