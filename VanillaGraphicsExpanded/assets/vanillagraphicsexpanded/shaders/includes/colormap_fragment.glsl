#ifndef VGE_COLORMAP_FRAGMENT_GLSL
#define VGE_COLORMAP_FRAGMENT_GLSL
// VGE-owned compatibility implementation, based on the installed Vintage Story liquid contract.
layout(location = 16) in vec2 climateColorMapUv;
layout(location = 17) in vec2 seasonColorMapUv;
layout(location = 20) in float frostAlpha;
layout(location = 18) in float seasonWeight;
layout(location = 19) in float heretemp;

/** Applies climate and seasonal tint with frost coverage to an unlit material sample. */
vec4 getColorMapped(sampler2D sourceTex, vec4 color) {
	vec4 tint = vec4(1);
	bool mapped = false;
	
	if (climateColorMapUv.x >= 0) {
		tint = texture(sourceTex, climateColorMapUv);
		mapped=true;
	}
	
	if (seasonColorMapUv.x >= 0 && seasonWeight > 0) {
		vec4 seasonColor = texture(sourceTex, seasonColorMapUv);
		tint = mix(tint, seasonColor, seasonWeight);
		mapped=true;
	}
	
	if (frostAlpha > 0) {
		float w = clamp((0.333 - heretemp) * 15, 0.0, 1.0);
		
		if (mapped) {
			tint.rgb = mix(tint.rgb, tint.rgb * (1 - frostAlpha) + vec3(1) * frostAlpha, w);
		} else {
			float b = (color.r + color.g + color.b) / 3.0;
			
			vec3 frostColor = vec3(b + frostAlpha*0.2);		
			float faw = frostAlpha * w;
			color.rgb = color.rgb * (1 - faw) + frostColor * faw;
			return color;
		}
	}
	
	return color * tint;
}

/** Applies frost coverage without climate or season tint. */
vec4 getFrosted(vec4 color) {
	if (heretemp < 0.333 && frostAlpha > 0) {
		float w = clamp((0.333 - heretemp) * 15, 0.0, 1.0);
		
		float b = (color.r + color.g + color.b) / 3.0;
		
		vec3 frostColor = vec3(b + frostAlpha*0.2);		
		float faw = frostAlpha * w;
		color.rgb = color.rgb * (1 - faw) + frostColor * faw;
	}
	return color;
}

#endif
