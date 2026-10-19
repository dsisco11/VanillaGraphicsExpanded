#version 430 core
layout(local_size_x=1) in;
layout(std140) uniform VolumeProbe { vec4 probeUvScale; };
uniform sampler3D probeDensity;
uniform sampler3D probeDirect;
uniform sampler3D probeDiffuse;
layout(std430) buffer ProbeResult { vec4 result[]; };
/** Reads actual hardware trilinear samples; no manual interpolation or native density path exists here. */
void main()
{
    vec3 uv=probeUvScale.xyz;
    result[0]=vec4(textureLod(probeDensity,uv,0).r,textureLod(probeDirect,uv,0).rg,0);
    vec4 diffuse=textureLod(probeDiffuse,uv,0);
    result[1]=vec4(diffuse.rgb*probeUvScale.w,diffuse.a);
}
