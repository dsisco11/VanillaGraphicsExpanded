#version 430 core
            #define VGE_SURFACE_PRIMARY_OUTPUTS 0
            #define SSAOLEVEL 0
            layout(location=0) out vec4 outColor;
            layout(location=1) out vec4 outGlow;
            const float extraGodray=1;
            float getSkyMurkiness() { return 0; }
            vec3 applyUnderwaterEffects(vec3 color,float murk) { return color; }
vec3 VgeResolveDisplay(vec3 value) { return value; }
vec3 VgeDitherDisplay(vec3 value, vec2 pixel) { return value; }
@import "../includes/atmosphere_sun_fragment.glsl"
void main() { VgeDrawAtmosphericSun(); }
