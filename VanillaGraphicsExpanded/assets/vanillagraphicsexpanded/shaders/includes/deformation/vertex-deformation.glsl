/** Explicit material inputs shared by owned stages and the native surface adapter. */
struct VgeVertexDeformationInputs {
    vec3 worldOrigin;
    float animationTime;
    float windPhase;
    float windDetailPhase;
    float liquidPhase;
    float windSpeed;
    float worldWarpStrength;
    float glitchStrength;
    float windStrength;
    float liquidStrength;
    int perceptionMode;
    float perceptionStrength;
};
/** Authored deformation shared by visible surfaces and shadow geometry.
 * Every displacement is a finite sum of bounded sine waves. World coordinates
 * anchor phases across chunk origins; frame counters never change mesh identity. */
vec3 VgePerceptionDeform(VgeVertexDeformationInputs inputs, vec3 p) {
    if (inputs.perceptionMode != 2) return p;
    float a = max(0.0, inputs.perceptionStrength) * 0.1;
    return p + a * sin(p.yzx * 0.4 + vec3(inputs.animationTime * 0.6));
}
/** Applies water motion with a positive denominator and an explicit wind contribution. */
vec4 VgeLiquidDeform(VgeVertexDeformationInputs inputs, bool windAffected, vec4 p, float divisor) {
    vec3 anchor = p.xyz + inputs.worldOrigin;
    float amplitude = abs(inputs.liquidStrength) / max(1.0, abs(divisor));
    if (windAffected) amplitude += abs(inputs.windStrength) * 0.05;
    p.y += amplitude * 0.5 * (sin(anchor.x * 0.7 + inputs.liquidPhase) + sin(anchor.z * 0.9 - inputs.liquidPhase * 0.8));
    p.xyz = VgePerceptionDeform(inputs, p.xyz);
    return p;
}
/** Decodes the public four-bit motion mode and three-bit bend weight. */
vec4 VgeVertexDeform(VgeVertexDeformationInputs inputs, int flags, vec4 p) {
#if VGE_VERTEX_DEFORMATION_ENABLED > 0
    int mode = int((uint(flags) >> 25u) & 15u);
    if (mode == 0 || mode == 8) return p;
    if (mode == 6 || mode == 12) return VgeLiquidDeform(inputs, true, p, 5.0);
    float weight = float((uint(flags) >> 29u) & 7u) / 7.0;
    float scale = (mode == 7) ? 0.01 : ((mode == 1 || mode == 9 || mode == 10 || mode == 13) ? 0.04 : 0.1);
    if (mode == 4 || mode == 5) scale *= weight * (mode == 5 ? 2.0 : 1.0);
    if (mode == 10) scale *= 1.0 - weight;
    vec3 anchor = p.xyz + inputs.worldOrigin;
    float wave = sin(dot(anchor.xz, vec2(0.23, 0.31)) + inputs.windPhase);
    float flutter = sin(dot(anchor.xz, vec2(1.7, -1.3)) + inputs.windDetailPhase);
    float amplitude = scale * abs(inputs.windStrength) * (1.0 + abs(inputs.windSpeed));
    p.xz += amplitude * vec2(wave + 0.25 * flutter, 0.35 * wave);
    if (mode == 3 || mode == 11) p.y += amplitude * 0.2 * flutter;
#endif
    return p;
}
/** Applies bounded world and perception effects after local material motion. */
vec4 VgeGlobalDeform(VgeVertexDeformationInputs inputs, vec4 p) {
#if VGE_VERTEX_DEFORMATION_ENABLED > 0
    vec3 anchor = p.xyz + inputs.worldOrigin;
    float amplitude = max(0.0, inputs.worldWarpStrength) * 0.05 + max(0.0, inputs.glitchStrength - 0.1) * 0.1;
    p.xyz += amplitude * sin(anchor.zxy * 0.13 + vec3(inputs.animationTime * 0.25));
    p.xyz = VgePerceptionDeform(inputs, p.xyz);
#endif
    return p;
}
