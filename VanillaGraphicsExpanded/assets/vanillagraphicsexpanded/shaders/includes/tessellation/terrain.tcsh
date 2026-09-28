// Runtime terrain stage: interface and engine prefix are supplied by the shader patch pipeline.
#if VGE_ENABLE_TESSELLATION
@import "./terrain_displacement.glsl"
@import "../vge_displacement_metadata.glsl"
patch out float vge_patchAmplitude;
patch out vec4 vge_patchRect;
uniform sampler2D vge_displacementRecords;
// Only full-tile corner triangles have all external edges inside the pinned band.
// Partial/cropped UV geometry can end inside a tile, where independent face normals
// would otherwise extrude its shared boundary in different directions.
int VgeTileCorner(vec2 value, vec2 lo, vec2 size, vec2 band) {
    bvec2 lower = lessThanEqual(abs(value - lo), band);
    bvec2 upper = lessThanEqual(abs(value - lo - size), band);
    if (any(equal(lower, upper))) return -1;
    return (upper.x ? 1 : 0) | (upper.y ? 2 : 0);
}
layout(vertices=3) out;
void main() {
    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;
    if (gl_InvocationID == 0) {
        // Resolve the authored tile from an interior UV, independently of engine SSBO face data.
        // Validate the complete triangle against that rectangle before any height sampling.
        vec4 rect;
        float amplitude;
        bool materialValid = VgeResolveDisplacement(vge_displacementTex, vge_displacementRecords,
            (uv[0] + uv[1] + uv[2]) / 3.0, rect, amplitude);
        vec2 lo = rect.xy;
        vec2 size = rect.zw;
        bool eligible = materialValid && VgeFinite2(vge_tessellationDistance) && vge_tessellationDistance.x >= 0.0
            && vge_tessellationDistance.y > vge_tessellationDistance.x && vge_tessellationPixels.z > 0.0
            && VgeRectValid(lo, size)
            && renderFlags[0] == renderFlags[1] && renderFlags[0] == renderFlags[2];
        eligible = eligible && vge_displacementEnabled != 0;
        eligible = eligible && VgeFinite(normal[0]) && dot(normal[0], normal[0]) > 0.00000001
            && all(equal(normal[0], normal[1])) && all(equal(normal[0], normal[2]));
        vec2 band = 1.0 / vec2(textureSize(vge_normalDepthTex, 0));
        int c0 = VgeTileCorner(uv[0], lo, size, band);
        int c1 = VgeTileCorner(uv[1], lo, size, band);
        int c2 = VgeTileCorner(uv[2], lo, size, band);
        eligible = eligible && c0 >= 0 && c1 >= 0 && c2 >= 0
            && c0 != c1 && c1 != c2 && c0 != c2;
        // Wind-deformed faces are excluded; this is the engine's authored wind-mode mask.
        eligible = eligible && ((renderFlags[0] & (15 << 25)) == 0);
        vec2 uvEdge1 = uv[1] - uv[0];
        vec2 uvEdge2 = uv[2] - uv[0];
        vec3 geometricCross = cross(worldPos[1].xyz - worldPos[0].xyz, worldPos[2].xyz - worldPos[0].xyz);
        eligible = eligible && VgeFinite2(uv[0]) && VgeFinite2(uv[1]) && VgeFinite2(uv[2])
            && all(greaterThanEqual(min(uv[0], min(uv[1], uv[2])), lo - vec2(0.00001)))
            && all(lessThanEqual(max(uv[0], max(uv[1], uv[2])), lo + size + vec2(0.00001)))
            && VgeFinite(worldPos[0].xyz) && VgeFinite(worldPos[1].xyz) && VgeFinite(worldPos[2].xyz)
            && abs(uvEdge1.x * uvEdge2.y - uvEdge1.y * uvEdge2.x) > 0.00000001
            && VgeFinite(geometricCross) && dot(geometricCross, geometricCross) > 0.00000001;
        eligible = eligible && amplitude > 0.0 && !isinf(amplitude) && !isnan(amplitude);
        vge_patchAmplitude = eligible ? min(amplitude, 0.05) : 0.0;
        vge_patchRect = eligible ? rect : vec4(0);
        gl_TessLevelOuter[0] = eligible ? VgeEdgeLevel(worldPos[1].xyz, worldPos[2].xyz) : 1.0;
        gl_TessLevelOuter[1] = eligible ? VgeEdgeLevel(worldPos[2].xyz, worldPos[0].xyz) : 1.0;
        gl_TessLevelOuter[2] = eligible ? VgeEdgeLevel(worldPos[0].xyz, worldPos[1].xyz) : 1.0;
        gl_TessLevelInner[0] = max(gl_TessLevelOuter[0], max(gl_TessLevelOuter[1], gl_TessLevelOuter[2]));
    }
}
#endif
