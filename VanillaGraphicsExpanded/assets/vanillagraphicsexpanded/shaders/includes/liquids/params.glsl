#ifndef VGE_LIQUID_PARAMS
#define VGE_LIQUID_PARAMS
#define DYNLIGHTS 100
#define SHADOWQUALITY 2
layout(std140, binding = 12) uniform VgeLiquidFrameParams
{
    mat4 projectionMatrix;
    mat4 toShadowMapSpaceMatrixNear;
    mat4 toShadowMapSpaceMatrixFar;
    vec4 liquidAnimation;
    vec4 liquidShadowRanges;
    vec4 liquidPlayer;
    vec4 liquidAtlas;
    vec4 liquidDepth;
    vec4 liquidSeason;
    vec4 liquidSun;
    vec4 liquidSolar;
    vec4 liquidEnvironment;
    vec4 liquidAerial;
    ivec4 liquidCounts;
    vec4 liquidPerception;
    vec4 liquidPerceptionPosition;
    vec4 colorMapRects[40];
    vec3 pointLights[100];
    vec3 pointLightColors[100];
    float fogSpheres[24];
    vec4 liquidMediumControl; // material lookup, composed volume, immutable refraction source, reserved
};
layout(std140, binding = 14) uniform VgeLiquidDrawParams
{
    mat4 modelViewMatrix;
    vec4 liquidOrigin;
};
#define origin liquidOrigin.xyz
#define forcedTransparency liquidOrigin.w
#define waterStillCounter liquidAnimation.x
#define waterFlowCounter liquidAnimation.y
#define windWaveCounter liquidAnimation.w
#define shadowRangeNear liquidShadowRanges.x
#define shadowRangeFar liquidShadowRanges.y
#define playerpos liquidPlayer.xyz
#define blockTextureSize liquidAtlas.xy
#define textureAtlasSize liquidAtlas.zw
#define zNear liquidDepth.x
#define zFar liquidDepth.y
#define frameSize liquidDepth.zw
#define seasonRel liquidSeason.x
#define seaLevel liquidSeason.y
#define atlasHeight liquidSeason.z
#define seasonTemperature liquidSeason.w
#define vge_atmosphereSunDirection liquidSun.xyz
#define vge_atmosphereSolar liquidSolar.xyz
#define vge_atmosphereEnvironment liquidEnvironment.xyz
#define vge_atmosphereAerialParams liquidAerial.xyz
#define cameraUnderwater liquidAerial.z
#define pointLightQuantity liquidCounts.x
#define fogSphereQuantity liquidCounts.y
#define psychedelicStrength liquidPerception.y
#define perceptionWorldOffset liquidPerceptionPosition.xyz
#endif
