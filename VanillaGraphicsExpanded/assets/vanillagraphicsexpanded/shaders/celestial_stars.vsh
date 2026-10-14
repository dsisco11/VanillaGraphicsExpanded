#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/celestial_draw.glsl"
layout(location=0) in vec4 starDirectionFlux;
layout(location=1) in vec4 starColor;
layout(location=0) flat out vec2 starCentre;
layout(location=1) flat out vec3 starPower;
layout(location=2) flat out vec3 starWorldDirection;
/** Projects the star once per instance; conservative two-pixel padding retains partial edge footprints. */
void main()
{
    starWorldDirection = mat3(celestialToWorld)*starDirectionFlux.xyz;
    vec4 clip = vgeFrame.projectionMatrix * vec4(mat3(vgeFrame.viewMatrix)*starWorldDirection,1.0);
    starPower = starColor.rgb * starDirectionFlux.w;
    starCentre = (clip.xy / max(clip.w,1e-20)*.5+.5)*vgeFrame.screenSize;
    const vec2 corners[6] = vec2[6](vec2(-1,-1),vec2(1,-1),vec2(1,1),vec2(-1,-1),vec2(1,1),vec2(-1,1));
    // Expand beyond the one-pixel kernel support so raster sample centres cover all intersected pixels.
    vec2 pixel = starCentre + corners[gl_VertexID] * 2.0;
    gl_Position = vec4(pixel/vgeFrame.screenSize*2.0-1.0,1.0,1.0);
    if (clip.w <= 0.0 || any(lessThan(starCentre,vec2(-2)))
        || any(greaterThan(starCentre,vgeFrame.screenSize+vec2(2)))) gl_Position = vec4(2,2,2,1);
}
