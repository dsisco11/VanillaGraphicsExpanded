#version 330 core

@import "./includes/lumon_octahedral.glsl"

in float vSkyIntensity;
in vec3 vAoDirWorld;
in float vAoConfidence;
in float vConfidence;
in float vMeanLogHitDistance;
flat in uint vFlags;

layout(location = 0) out vec4 outProbeVis0;
layout(location = 1) out vec2 outProbeDist0;
layout(location = 2) out vec2 outProbeMeta0;

void main()
{
    // ShortRangeAO: oct-encoded direction + confidence.
    vec3 dir = normalize(vAoDirWorld);
    vec2 aoUv = lumonDirectionToOctahedralUV(dir);
    outProbeVis0 = vec4(aoUv, vSkyIntensity, vAoConfidence);

    // Distance: log(dist+1), reserved.
    outProbeDist0 = vec2(vMeanLogHitDistance, 0.0);

    // Meta: confidence + uintBitsToFloat(flags).
    outProbeMeta0 = vec2(vConfidence, uintBitsToFloat(vFlags));
}
