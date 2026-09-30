#ifndef VGE_PBR_LIQUID_GLSL
#define VGE_PBR_LIQUID_GLSL



@import "./pbr_liquid_optics.glsl"

/** Uses the same engine cascade coordinates as liquid geometry, with no ambient brightness floor. */
float VgeLiquidVisibility()
{
    float blocked = 0.0;
#if SHADOWQUALITY > 0
    if (shadowCoordsFar.w > 0.0) blocked += (1.0 - texture(shadowMapFar, vec3(shadowCoordsFar.xy, shadowCoordsFar.z - .0009))) * shadowCoordsFar.w;
#endif
#if SHADOWQUALITY > 1
    if (shadowCoordsNear.w > 0.0) blocked += (1.0 - texture(shadowMapNear, vec3(shadowCoordsNear.xy, shadowCoordsNear.z - .0005))) * shadowCoordsNear.w;
#endif
    return 1.0 - clamp(blocked, 0.0, 1.0);
}

/** Evaluates a liquid interface and a bounded homogeneous medium using existing OIT transmission. */
vec4 VgeLiquidSurface(vec4 textureColor, vec4 material, bool lava, bool fullAlpha)
{
    mat3 toWorld = transpose(mat3(modelViewMatrix));
    vec3 V = normalize(toWorld * -vge_viewPosition);
    vec3 geometric = cross(dFdx(fWorldPos), dFdy(fWorldPos));
    float normSquared = dot(geometric, geometric);
    vec3 N = normSquared > 1e-16 ? geometric * inversesqrt(normSquared) : normalize(fragNormal);
    if (dot(N, V) < 0.0) N = -N;
    vec3 tint = clamp(VgeSrgbToLinear(textureColor.rgb), vec3(0), vec3(1));
    float roughness = clamp(material.r, .04, 1.0);
    float emission = max(material.b, glowLevel);
    // Only explicitly transmitting, non-emissive liquid materials select water optics.
    bool water = material.a > 0.0 && !lava && !fullAlpha && emission <= 0.0;
    bool underwater = cameraUnderwater > .7;
    float cosine = clamp(dot(N, V), 0.0, 1.0);
    float fresnel = water ? VgeLiquidFresnel(cosine, underwater) : fresnelSchlick(cosine, vec3(.04)).r;
    vec3 F0 = vec3(water ? .02037 : .04);
    vec3 L = normalize(vge_atmosphereSunDirection);
    vec3 solar = max(vge_atmosphereSolar, vec3(0)) * vge_skyVisibility * VgeLiquidVisibility();
    vec3 reflected = cookTorranceBRDF(N, V, L, F0, roughness) * solar * max(dot(N,L), 0.0);
    vec3 bodyLight = vge_blockIrradiance + vge_atmosphereEnvironment * vge_skyVisibility;
    vec3 diffuse = tint * solar * max(dot(N,L), 0.0) / 3.14159265359;
#if DYNLIGHTS > 0
    for (int i = 0; i < min(pointLightQuantity, DYNLIGHTS); ++i)
    {
        vec3 delta = pointLights[i] - vge_viewPosition;
        float d2 = max(dot(delta, delta), .0001);
        vec3 direction = normalize(toWorld * delta);
        vec3 light = pointLightColors[i] * min(1.0 / d2, 1.0);
        reflected += cookTorranceBRDF(N,V,direction,F0,roughness) * light * max(dot(N,direction),0.0);
        diffuse += tint * light * max(dot(N,direction),0.0);
    }
#endif
    // Environment reflection is intentionally local and bounded: no active framebuffer feedback or SSR holes.
    reflected += max(vge_atmosphereEnvironment, vec3(0)) * vge_skyVisibility * fresnel * (1.0 - .5 * roughness);
    float alpha = 1.0;
    vec3 radiance;
    if (water)
    {
        vec2 screenUv = clamp(gl_FragCoord.xy / frameSize, vec2(0), vec2(1));
        float thickness = VgeLiquidThickness(texture(depthTex, screenUv).r, gl_FragCoord.z, vge_viewPosition, underwater);
        // Albedo tint supplies an art-directed extinction spectrum; this is not measured water chemistry.
        vec3 extinction = mix(vec3(.08), vec3(.8), vec3(1) - tint);
        vec3 transmittance = exp(-extinction * thickness);
        float transmitted = dot(transmittance, vec3(.2126,.7152,.0722)) * clamp(material.a,0.0,1.0);
        alpha = clamp(1.0 - (1.0 - fresnel) * transmitted, .001, 1.0);
        vec3 scattering = tint * bodyLight * (vec3(1) - transmittance) * (1.0 - fresnel);
        // OIT multiplies by alpha. Normalize only our surface source, not the background transmission.
        radiance = (reflected + scattering) / alpha;
    }
    else
    {
        // Milk/dyes/oil and emissive lava retain distinct opaque body response; no water absorption is inferred.
        radiance = reflected + diffuse + tint * bodyLight + tint * max(emission, 0.0);
    }
    if (!underwater)
        radiance = VgeApplyAerial(radiance, toWorld * vge_viewPosition, vge_skyVisibility,
            vge_atmosphereAerialParams.xy, vge_atmosphereSunDirection);
    return vec4(VgeDitherDisplay(VgeResolveDisplay(max(radiance,vec3(0))), gl_FragCoord.xy), alpha);
}
#endif
