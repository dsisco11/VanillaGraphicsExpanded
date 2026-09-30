#ifndef VGE_FOG_SPHERES_GLSL
#define VGE_FOG_SPHERES_GLSL
// VGE-owned compatibility implementation, based on the installed Vintage Story liquid contract.
/** Integrates local spherical fog density along the camera-to-surface segment. */
float getSpheresFogAmount(vec3 worldPos) {
	if (fogSphereQuantity == 0) return 0.0;
	
	float depth = length(worldPos);
	float fogamount = 0;
	
	for (int i = 0; i < fogSphereQuantity; i++) {
		vec3 L = vec3(fogSpheres[i * 8], fogSpheres[i * 8 +1], fogSpheres[i * 8 + 2]);
		float radius = fogSpheres[i * 8 + 3];
		float density = fogSpheres[i * 8 + 4];		

		vec3 D = normalize(worldPos.xyz);
		
		float outsideness = length(L) / radius;
		float tca = dot(L, D);

		if (outsideness < 1) {
			float thc=0;
			if (tca >= 0)  {
				thc = sqrt(radius*radius - dot(L,L) + tca*tca);
			} else {
				thc = sqrt(radius*radius - dot(L,L));
			}
			
			thc = min(thc, depth);
			fogamount += thc * density;
		} else {
			
			if (tca >= 0) {
				float d2 = dot(L, L) - tca * tca;
				if (d2 < radius * radius) {
					float thc = sqrt(radius * radius - d2);
					float t0 = tca - thc;
					float t1 = tca + thc;
					
					float tf = depth;
					
					t1 = min(tf, t1);	
					
					if (tf > t0) {
						fogamount += (t1 - t0) * density;
					}
				}
			}
		}
	}	
	
	return fogamount;
}

/** Blends local fog-volume color along the visible segment. */
vec4 applySpheresFog(vec4 color, float standardFogAmount, vec3 worldPos) {
	if (fogSphereQuantity == 0) return color;
	
	float depth = length(worldPos);
	
	for (int i = 0; i < fogSphereQuantity; i++) {
		vec3 L = vec3(fogSpheres[i * 8], fogSpheres[i * 8 +1], fogSpheres[i * 8 + 2]);
		float radius = fogSpheres[i * 8 + 3];
		float density = fogSpheres[i * 8 + 4];
		vec3 fogrgb = vec3(fogSpheres[i * 8 + 5], fogSpheres[i * 8 + 6], fogSpheres[i * 8 + 7]);
		
		float fogamount = 0;

		vec3 D = normalize(worldPos.xyz);
		
		float outsideness = length(L) / radius;
		float tca = dot(L, D);

		if (outsideness < 1) {
			float thc=0;
			if (tca >= 0)  {
				thc = sqrt(radius*radius - dot(L,L) + tca*tca);
			} else {
				thc = sqrt(radius*radius - dot(L,L));
			}
			
			thc = min(thc, depth);
			fogamount = thc * density;
		} else {
			
			if (tca >= 0) {
				float d2 = dot(L, L) - tca * tca;
				if (d2 < radius * radius) {
					float thc = sqrt(radius * radius - d2);
					float t0 = tca - thc;
					float t1 = tca + thc;
					
					float tf = depth;
					
					t1 = min(tf, t1);	
					
					if (tf > t0) {
						fogamount = (t1 - t0) * density;
					}
				}
			}
		}
		
		color.rgb = mix(color.rgb, fogrgb, clamp(fogamount - (standardFogAmount - fogamount), 0, 1));
	}	

	return color;
}

#endif
