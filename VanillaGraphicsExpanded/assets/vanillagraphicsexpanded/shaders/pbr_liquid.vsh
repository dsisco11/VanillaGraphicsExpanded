#version 450 core
@import "./includes/liquids/params.glsl"
layout(location = 0) out vec2 uv;
layout(location = 1) out vec2 uvSize;
layout(location = 2) flat out vec2 uvBase;
layout(location = 3) out vec2 flowVectorf;
layout(location = 4) out float waterStillCounterOff;
layout(location = 5) out vec3 fragWorldPos;
layout(location = 6) out vec3 fWorldPos;
layout(location = 7) out vec3 fragNormal;
layout(location = 8) flat out int waterFlags;
layout(location = 9) out vec3 vge_viewPosition;
layout(location = 10) out vec3 vge_blockIrradiance;
layout(location = 11) out float vge_skyVisibility;
layout(location = 12) out float glowLevel;
layout(location = 14) out vec4 shadowCoordsNear;
layout(location = 15) out vec4 shadowCoordsFar;
layout(location = 0) in vec3 xyz;
layout(location = 1) in vec2 uvIn;
layout(location = 2) in vec4 rgbaLightIn;
layout(location = 3) in int renderFlags;
layout(location = 4) in vec2 flowVector;
layout(location = 5) in int colormapData;
layout(location = 6) in int waterFlagsIn;
@import "./includes/vertex_flags.glsl"
@import "./includes/colormap_noise.glsl"
@import "./includes/colormap_vertex.glsl"
@import "./includes/pbr_shadowcoords.glsl"
/** Prepares the existing liquid mesh layout without vanilla lighting or Fresnel alpha. */
void main()
{
    vec4 truePos = vec4(xyz + origin, 1);
    vec4 worldPos = truePos;
    // Undisplaced mesh geometry until the shared color/depth wave model is implemented.
    vec4 cameraPos = modelViewMatrix * worldPos;
    gl_Position = projectionMatrix * cameraPos;
    waterStillCounterOff = smoothstep(0, 1, abs(mod(waterStillCounter + length(worldPos.xz + playerpos.xz) / 3.0, 2.0) - 1.0));
    if ((waterFlagsIn & 2) == 0) waterStillCounterOff = 1;
    fragWorldPos = worldPos.xyz + perceptionWorldOffset;
    fWorldPos = worldPos.xyz;
    uv = uvIn;
    uvSize = vec2((waterFlagsIn >> 10) & 255, (waterFlagsIn >> 18) & 255) / 255.0 * blockTextureSize;
    uvBase = uv - uvSize;
    flowVectorf = flowVector;
    waterFlags = waterFlagsIn;
    fragNormal = unpackNormal(renderFlags);
    glowLevel = float(renderFlags & GlowLevelBitMask) / 256.0;
    vge_viewPosition = cameraPos.xyz;
    vge_blockIrradiance = max(rgbaLightIn.rgb, vec3(0));
    vge_skyVisibility = clamp(rgbaLightIn.a, 0, 1);
    pbrCalcShadowMapCoords(worldPos.xyz, shadowCoordsNear, shadowCoordsFar);
    calcColorMapUvs(colormapData, truePos + vec4(playerpos, 1), rgbaLightIn.a, false);
    gl_Position.w += .0008 / max(.1, gl_Position.z);
}
