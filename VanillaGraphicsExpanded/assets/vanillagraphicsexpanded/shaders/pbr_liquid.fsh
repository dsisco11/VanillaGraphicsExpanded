#version 450 core
@import "./includes/liquids/params.glsl"
layout(location = 0) in vec2 uv;
layout(location = 1) in vec2 uvSize;
layout(location = 2) flat in vec2 uvBase;
layout(location = 3) in vec2 flowVectorf;
layout(location = 4) in float waterStillCounterOff;
layout(location = 5) in vec3 fragWorldPos;
layout(location = 6) in vec3 fWorldPos;
layout(location = 7) in vec3 fragNormal;
layout(location = 8) flat in int waterFlags;
layout(location = 9) in vec3 vge_viewPosition;
layout(location = 10) in vec3 vge_blockIrradiance;
layout(location = 11) in float vge_skyVisibility;
layout(location = 12) in float glowLevel;
layout(location = 14) in vec4 shadowCoordsNear;
layout(location = 15) in vec4 shadowCoordsFar;
layout(location = 100, binding = 0) uniform sampler2D terrainTex;
layout(location = 101, binding = 1) uniform sampler2D depthTex;
layout(location = 102, binding = 2) uniform sampler2D vge_materialParamsTex;
layout(location = 49, binding = 3) uniform sampler2DShadow shadowMapNear;
layout(location = 48, binding = 4) uniform sampler2DShadow shadowMapFar;
@import "./includes/vertex_flags.glsl"
@import "./includes/colormap_fragment.glsl"
@import "./includes/texture_animation.glsl"
@import "./includes/fog_spheres.glsl"
@import "./includes/perception_fragment.glsl"
@import "./includes/oit.glsl"
@import "./includes/pbr_color.glsl"
@import "./includes/pbr_common.glsl"
@import "./includes/atmosphere_aerial.glsl"
@import "./includes/pbr_liquid.glsl"
/** Samples the animated material before PBR optics and engine-compatible OIT accumulation. */
void main()
{
	vec4 texColor;
	
	float vn = max(0, 0.9 - abs(fragNormal.y));
	float wfc;
	
	bool isLava = (waterFlags & LiquidIsLavaBitMask) > 0;
	bool fullAlpha = (waterFlags & LiquidFullAlphaBitMask) > 0;
	
	if (isLava) wfc = waterFlowCounter * 0.1 * (1 + 5 * vn);
	else wfc = waterFlowCounter * (1 + 5 * vn);

	float flowSpeed = length(flowVectorf);
	if (flowSpeed > 0.001) {
		vec2 flowVec = normalize(flowVectorf) * flowSpeed;
		
		if (fragNormal.y < 0) wfc*=-1;
		
        texColor = texture(terrainTex, VgeAnimatedAtlasUv(uvBase, uv - uvBase,
            flowVec * wfc * blockTextureSize, blockTextureSize, textureAtlasSize));

	} else {
		
		vec2 uvxOffset = 
			clamp(
				blockTextureSize - uvSize,
				vec2(1 / textureAtlasSize), 
				blockTextureSize - 1 / textureAtlasSize)
		;
		
		texColor = texture(terrainTex, uv) * waterStillCounterOff + (1-waterStillCounterOff) * texture(terrainTex, uvBase + uvxOffset);				
	}
	
	texColor = getColorMapped(terrainTex, texColor);

	if (psychedelicStrength > 0.00001) texColor = applyPsychedelicEffect(texColor, fragWorldPos, 0);

    vec4 material = texture(vge_materialParamsTex, uv);
    vec4 liquid = VgeLiquidSurface(texColor, material, isLava, fullAlpha);
    liquid = applySpheresFog(liquid, 0.0, fWorldPos);
    liquid.a *= 1.0 - forcedTransparency;
    writeOit(liquid, max(glowLevel, clamp(material.b, 0, 1)));
}
