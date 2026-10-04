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
layout(location = 21) in vec2 vge_wavePosition;
layout(location = 22) in vec2 vge_waveWeights;
layout(binding = 0) uniform sampler2D terrainTex;
layout(binding = 1) uniform sampler2D depthTex;
layout(binding = 2) uniform sampler2D vge_materialParamsTex;
layout(binding = 7) uniform sampler2D vge_waterMediumIndices;
layout(binding = 8) uniform sampler2D vge_waterMediumRecords;
layout(binding = 3) uniform sampler2DShadow shadowMapNear;
layout(binding = 4) uniform sampler2DShadow shadowMapFar;
@import "./includes/vertex_flags.glsl"
@import "./includes/colormap_fragment.glsl"
@import "./includes/texture_animation.glsl"
@import "./includes/fog_spheres.glsl"
@import "./includes/perception_fragment.glsl"
#if VGE_LIQUID_CAPTURE_MODE == 3
layout(location = 0) out vec4 outWaterOpticalDepth;
layout(location = 1) out vec4 outWaterSource;
#elif VGE_LIQUID_CAPTURE_MODE > 0
layout(location = 0) out vec4 outSpecularCapture;
#else
@import "./includes/oit.glsl"
#endif
@import "./includes/pbr_color.glsl"
@import "./includes/pbr_common.glsl"
@import "./includes/liquids/waves.glsl"
@import "./includes/pbr_shadowcoords.glsl"
@import "./includes/atmosphere_aerial.glsl"
@import "./includes/pbr_liquid.glsl"
/** Samples the animated material before PBR optics and engine-compatible OIT accumulation. */
void main()
{
#if VGE_LIQUID_CAPTURE_MODE == 2
    outSpecularCapture = vec4(1.0);
    return;
#endif
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
#if VGE_LIQUID_CAPTURE_MODE == 3
    // Sum signed boundary antiderivatives. An entry adds the distance to the opaque
    // receiver; an exit subtracts it. Air gaps cancel without depth-difference assumptions.
    if (material.a <= 0.0 || isLava || fullAlpha) discard;
    float opaqueDepth = texelFetch(depthTex, ivec2(gl_FragCoord.xy), 0).r;
    float rayScale = length(vge_viewPosition) / max(abs(vge_viewPosition.z), .001);
    // For sky, the far plane is only the boundary-capture extent. Composition
    // accepts a finite water segment only when the captured winding returns to air.
    float receiverDistance = (opaqueDepth >= .999999 ? zFar : VgeLiquidViewDepth(opaqueDepth)) * rayScale;
    float remaining = max(receiverDistance - length(vge_viewPosition), 0.0);
    if (remaining <= 0.0) discard;
    vec3 geometric = cross(dFdx(fWorldPos), dFdy(fWorldPos));
    float normalSquared = dot(geometric, geometric);
    vec3 outward = normalSquared > 1e-16 ? geometric * inversesqrt(normalSquared) : normalize(fragNormal);
    if (dot(outward, fragNormal) < 0.0) outward = -outward;
    vec3 toEye = normalize(-vge_viewPosition);
    float orientation = dot(mat3(modelViewMatrix) * outward, toEye) >= 0.0 ? 1.0 : -1.0;
    VgeWaterMedium medium = VgeWaterMaterial(uv);
    vec3 source = vec3(0);
    // Published coefficients already include density. Clear and zero-density
    // media still accumulate their signed geometry/extinction, but need no source
    // illumination or shadow samples when every scattering channel is zero.
    if (any(greaterThan(medium.scattering, vec3(0))))
    {
        vec3 eyeWorld = transpose(mat3(modelViewMatrix)) * toEye;
        source = max(vge_atmosphereSolar, vec3(0)) * vge_skyVisibility * VgeLiquidVisibility()
            * VgeWaterPhase(dot(-normalize(vge_atmosphereSunDirection), eyeWorld), medium.anisotropy)
            + max(vge_blockIrradiance + vge_atmosphereEnvironment * vge_skyVisibility, vec3(0)) / 12.56637061436;
    }
    outWaterOpticalDepth = vec4((medium.absorption + medium.scattering) * remaining, remaining) * orientation;
    outWaterSource = vec4(medium.scattering * source * remaining, 1.0) * orientation;
    return;
#else
    vec4 liquid = VgeLiquidSurface(texColor, material, isLava, fullAlpha);
#if VGE_LIQUID_CAPTURE_MODE > 0
    outSpecularCapture = liquid;
#else
    liquid = applySpheresFog(liquid, 0.0, fWorldPos, liquidMediumControl.w > .5);
    liquid.a *= 1.0 - forcedTransparency;
    writeOit(liquid, max(glowLevel, clamp(material.b, 0, 1)));
#endif
#endif
}
