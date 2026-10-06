#version 450 core
@import "../includes/atmosphere_solar_disk.glsl"
uniform float elevation;
            layout(location=0) out vec4 result;
            void main() {
                float visible=atmSunVisibility(elevation,0);
                result=vec4(visible,atmSunVisibleElevation(elevation,0,visible),0,1);
            }
