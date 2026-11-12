/** Decodes the signed-magnitude normal in the native public vertex flag ABI. */
vec3 VgeDecodeVertexNormal(int vge_vertexFlags) {
    vec3 n;
    for (int axis = 0; axis < 3; axis++) {
        uint component = (uint(vge_vertexFlags) >> uint(13 + 4 * axis)) & 15u;
        n[axis] = float(component >> 1u) * ((component & 1u) == 0u ? 1.0 : -1.0);
    }
    return dot(n, n) > 0.0 ? normalize(n) : vec3(0.0);
}
#if VGE_TERRAIN_PACKED_INPUT > 0
struct ShadowFace { vec3 position; int packedUv; vec3 edgeU; int packedSize; ivec4 vge_vertexFlags; vec3 edgeV; int colorMap; };
layout(std430) readonly buffer VgeShadowFaces { ShadowFace vge_shadowFaces[]; };
#else
layout(location=0) in vec3 vge_vertexPosition;
layout(location=1) in vec2 vge_vertexUv;
layout(location=3) in int vge_vertexFlags;
#endif
out vec2 vge_shadowUv;
out vec4 vge_shadowPosition;
#if VGE_TERRAIN_SUBDIVISION > 0
out vec3 vge_shadowNormal;
flat out int vge_shadowVertexFlags;
flat out vec2 vge_uvBase;
flat out vec2 vge_uvExtent;
#endif
/** Reads the native quad ABI without owning or copying its geometry allocation. */
void main() {
    vec3 position;
    int vertexFlags;
#if VGE_TERRAIN_PACKED_INPUT > 0
    int corner = gl_VertexID % 4;
    ShadowFace face = vge_shadowFaces[gl_VertexID / 4];
    vec2 cornerBits = vec2(corner == 1 || corner == 2 ? 1.0 : 0.0, corner >= 2 ? 1.0 : 0.0);
    position = face.position + 2.0 * (cornerBits.x * face.edgeU + cornerBits.y * face.edgeV);
    vertexFlags = face.vge_vertexFlags[corner];
    // Each extent is a signed 15-bit field; bit 15 is the UV rotation flag.
    float du = (float(bitfieldExtract(face.packedSize, 0, 15)) - 1e-8) / 32768.0;
    float dv = (float(bitfieldExtract(face.packedSize, 16, 15)) - 1e-8) / 32768.0;
    vec2 uvCorner = (uint(face.packedSize) & 0x8000u) != 0u ? cornerBits : cornerBits.yx;
    vec2 extent = vec2(du, dv);
    vge_shadowUv = vec2(uint(face.packedUv) & 65535u, uint(face.packedUv) >> 16u) / 32768.0;
    vec2 delta = uvCorner * extent;
    vge_shadowUv += delta - sign(delta) * vec2(vge_atlasPaddingX, vge_atlasPaddingY);
#else
    position = vge_vertexPosition;
    vertexFlags = vge_vertexFlags;
    vge_shadowUv = vge_vertexUv;
#endif
    vec4 vge_materialPosition = VgeGlobalDeform(VgeShadowDeformationInputs(), VgeVertexDeform(VgeShadowDeformationInputs(), vertexFlags, vec4(position + vge_chunkOrigin, 1.0)));
    gl_Position = vge_materialTransform * vge_materialPosition;
#if VGE_TERRAIN_SUBDIVISION > 0
    vge_shadowPosition = vge_materialPosition;
    vge_shadowNormal = VgeDecodeVertexNormal(vertexFlags);
    vge_shadowVertexFlags = vertexFlags;
    vge_uvBase = vec2(-1.0);
    vge_uvExtent = vec2(0.0);
#else
    vge_shadowPosition = vge_shadowOriginTransform * vge_materialPosition;
#endif
}
