#version 330 core
@import "../includes/ambient_occlusion.glsl"
layout(std140,binding=28) uniform AoIntegralTestInputs {vec4 angles;};
layout(location=0) out vec4 result;
/** Exposes production integration and normalization for independent numerical quadrature. */
void main() {
    if(angles.w>0.5) {
        result=vec4(VgeAoRelaxHorizon(angles.x,angles.y,angles.z),0,0,1);
        return;
    }
    float low=max(-1.570796327,angles.x-1.570796327);
    float high=min(1.570796327,angles.x+1.570796327);
    float baseline=VgeAoIntegral(low,high,angles.x);
    float visible=VgeAoIntegral(angles.y,angles.z,angles.x);
    result=vec4(visible,baseline,visible/baseline,1);
}
