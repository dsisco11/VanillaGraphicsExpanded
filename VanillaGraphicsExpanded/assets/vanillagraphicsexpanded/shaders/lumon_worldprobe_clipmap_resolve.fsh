#version 330 core

#include "shaders/includes/lumon_octahedral.glsl"

in vec4 vShR;
in vec4 vShG;
in vec4 vShB;
in vec3 vAoDirWorld;
in float vAoConfidence;
in float vConfidence;
in float vMeanLogHitDistance;
flat in uint vFlags;

layout(location = 0) out vec4 outProbeSH0;
layout(location = 1) out vec4 outProbeSH1;
layout(location = 2) out vec4 outProbeSH2;
layout(location = 3) out vec4 outProbeVis0;
layout(location = 4) out vec2 outProbeDist0;
layout(location = 5) out vec2 outProbeMeta0;

void main()
{
    // L1 SH: per channel vec4(c0, cY, cZ, cX)
    outProbeSH0 = vec4(vShR.x, vShG.x, vShB.x, vShR.y);
    outProbeSH1 = vec4(vShG.y, vShB.y, vShR.z, vShG.z);
    outProbeSH2 = vec4(vShB.z, vShR.w, vShG.w, vShB.w);

    // ShortRangeAO: oct-encoded direction + confidence.
    vec3 dir = normalize(vAoDirWorld);
    vec2 aoUv = lumonDirectionToOctahedralUV(dir);
    outProbeVis0 = vec4(aoUv, 0.0, vAoConfidence);

    // Distance: log(dist+1), reserved.
    outProbeDist0 = vec2(vMeanLogHitDistance, 0.0);

    // Meta: confidence + uintBitsToFloat(flags).
    outProbeMeta0 = vec2(vConfidence, uintBitsToFloat(vFlags));
}
