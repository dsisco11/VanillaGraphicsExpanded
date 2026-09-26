#version 330 core

vec2 uv;
out vec4 outColor;

@import "./includes/lumon_common.glsl"
@import "./includes/lumon_sh.glsl"
@import "./includes/lumon_probe_atlas_meta.glsl"
@import "./includes/velocity_common.glsl"
@import "./includes/lumon_pbr.glsl"
@import "./includes/vge_global_defines.glsl"
@import "./includes/squirrel3.glsl"
@import "./includes/lumonscene_surface_cache.glsl"
@import "./includes/lumonscene_material_packing.glsl"
@import "./includes/lumon_debug_uniforms.glsl"


/** Implements render lumon scene material atlas all debug for its explicit view entrypoint. */
vec4 renderLumonSceneMaterialAtlasAllDebug()
{
    if (vge_lumonSceneEnabled == 0)
    {
        return vec4(0.2, 0.0, 0.2, 1.0);
    }

    ivec3 sz = textureSize(vge_lumonSceneMaterialAtlas, 0);
    int layers = max(1, sz.z);

    // Grid dims: ceil(sqrt(layers)) by ceil(layers / gridX)
    int gridX = int(ceil(sqrt(float(layers))));
    int gridY = int(ceil(float(layers) / float(max(1, gridX))));

    vec2 uv01 = gl_FragCoord.xy / screenSize;

    int cx = int(floor(uv01.x * float(gridX)));
    int cy = int(floor(uv01.y * float(gridY)));
    if (cx < 0 || cy < 0 || cx >= gridX || cy >= gridY)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    int layer = cy * gridX + cx;
    if (layer < 0 || layer >= layers)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    // Local UV inside the selected cell.
    vec2 cellUv = fract(vec2(uv01.x * float(gridX), uv01.y * float(gridY)));

    // Draw a subtle grid border.
    float px = 1.0 / max(1.0, screenSize.x);
    float py = 1.0 / max(1.0, screenSize.y);
    if (cellUv.x < px || cellUv.y < py || (1.0 - cellUv.x) < px || (1.0 - cellUv.y) < py)
    {
        return vec4(0.0, 0.0, 0.0, 1.0);
    }

    vec4 mat = texture(vge_lumonSceneMaterialAtlas, vec3(cellUv, float(layer)));
    uint surfaceId = VgeLumonSceneUnpackSurfaceIdFromMaterialAtlas(mat);
    uvec4 s = texelFetch(vge_lumonSceneSurfaceLut, VgeLumonSceneSurfaceLutUv(surfaceId), 0);
    vec3 albedo = vec3(s.xyz) * (1.0 / 255.0);
    return vec4(clamp(albedo, 0.0, 1.0), 1.0);
}

/** Renders only the LumonSceneMaterialAtlasAll view; mode selection occurs before program loading. */
void main()
{
    uv = gl_FragCoord.xy / screenSize;
    vec2 screenPos = uv * screenSize;
    outColor = renderLumonSceneMaterialAtlasAllDebug();
}
