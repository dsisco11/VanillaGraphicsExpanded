#version 430 core
            #define VGE_SURFACE_PRIMARY_OUTPUTS 0
            #define SSAOLEVEL 0
            layout(location=0) out vec4 outColor;
            layout(location=1) out vec4 outGlow;
            const float extraGodray=1;
            float getSkyMurkiness() { return 0; }
            vec3 applyUnderwaterEffects(vec3 color,float murk) { return color; }
@import "../includes/pbr_color.glsl"
@import "../includes/atmosphere_sun_fragment.glsl"
void main() { VgeDrawAtmosphericSun(); }
