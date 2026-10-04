#ifndef VGE_PBR_LIQUID_GLSL
#define VGE_PBR_LIQUID_GLSL



@import "./pbr_liquid_optics.glsl"
@import "./liquids/medium_material.glsl"
@import "./liquids/refraction.glsl"
@import "./liquids/uv_distortion.glsl"
layout(location = 120) uniform int vge_waterRefractionQuality;

/** Uses the same engine cascade coordinates as liquid geometry, with no ambient brightness floor. */
float VgeLiquidVisibility()
{
    float blocked = 0.0;
    // Cascade coverage is nonlinear, so evaluate it at the fragment instead of interpolating vertex weights.
    vec4 shadowCoordsNear;
    vec4 shadowCoordsFar;
    pbrCalcShadowMapCoords(fWorldPos, shadowCoordsNear, shadowCoordsFar);
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
    float emission = max(material.b, glowLevel);
    // Transmission selects the water optical model; emission is an independent material property.
    bool water = material.a > 0.0 && !lava && !fullAlpha;
    vec3 geometric = cross(dFdx(fWorldPos), dFdy(fWorldPos));
    float normSquared = dot(geometric, geometric);
    vec3 N = normSquared > 1e-16 ? geometric * inversesqrt(normSquared) : normalize(fragNormal);
    if (water && fragNormal.y > 0.7)
    {
        // Evaluate the continuous wave normal here; vertex interpolation exposes coarse mesh triangles in highlights.
        vec3 unusedPosition;
        VgeLiquidWaveSurface(vec3(vge_wavePosition.x, 0.0, vge_wavePosition.y),
            vge_waveWeights, unusedPosition, N);
    }
    if (dot(N, V) < 0.0) N = -N;
    vec3 tint = clamp(VgeSrgbToLinear(textureColor.rgb), vec3(0), vec3(1));
    float roughness = clamp(material.r, .04, 1.0);
    bool underwater = cameraUnderwater > .7;
    float cosine = clamp(dot(N, V), 0.0, 1.0);
    float fresnel = water ? VgeLiquidFresnel(cosine, underwater) : fresnelSchlick(cosine, vec3(.04)).r;
    vec3 F0 = vec3(water ? .02037 : .04);
    vec3 L = normalize(vge_atmosphereSunDirection);
    vec3 solar = max(vge_atmosphereSolar, vec3(0)) * vge_skyVisibility * VgeLiquidVisibility();
    vec3 reflected = cookTorranceBRDF(N, V, L, F0, roughness) * solar * max(dot(N,L), 0.0);
#if VGE_LIQUID_CAPTURE_MODE == 1
    // The GPU test captures this direct-sun term before medium and OIT composition.
    return vec4(VgeSceneOutput(reflected, gl_FragCoord.xy, liquidMediumControl.w > .5), 1.0);
#endif
    vec3 bodyLight = vge_blockIrradiance + vge_atmosphereEnvironment * vge_skyVisibility;
    VgeWaterMedium medium = water ? VgeWaterMaterial(uv) : VgeWaterMedium(vec3(0), vec3(0), 0.0);
    VgeWaterReceiver receiver = VgeWaterReceiver(false, VGE_WATER_RECEIVER_NONE,
        vec3(0), vge_viewPosition, vec3(0), 0.0, 0.0);
    if (water && liquidMediumControl.z > .5 && fresnel < 1.0)
    {
        vec3 normalVS = normalize(mat3(modelViewMatrix) * N);
        receiver = vge_waterRefractionQuality == 0
            ? VgeWaterUvRefraction(vge_viewPosition, normalVS, underwater)
            : VgeWaterRefraction(vge_viewPosition, normalVS, underwater);
    }
    vec3 waterOutgoing = receiver.valid
        ? VgeWaterOutgoingDirection(vge_viewPosition, receiver.refractedDirectionVS, underwater, toWorld) : V;
    // Incoming sunlight travels along -L, outgoing photons toward V. Receiver shadow/sky
    // visibility bounds this local source; it is not a volumetric shadow march.
    vec3 mediumSource = solar * VgeWaterPhase(dot(-L, V), medium.anisotropy)
        + max(bodyLight, vec3(0)) / 12.56637061436;
    vec3 refractedSource = solar * VgeWaterPhase(dot(-L, waterOutgoing), medium.anisotropy)
        + max(bodyLight, vec3(0)) / 12.56637061436;
    vec3 diffuse = tint * solar * max(dot(N,L), 0.0) / 3.14159265359;
#if DYNLIGHTS > 0
    for (int i = 0; i < min(pointLightQuantity, DYNLIGHTS); ++i)
    {
        vec3 delta = pointLights[i] - vge_viewPosition;
        float d2 = max(dot(delta, delta), .0001);
        vec3 direction = normalize(toWorld * delta);
        vec3 light = pointLightColors[i] * min(1.0 / d2, 1.0);
        // Local lights retain the existing unshadowed engine approximation.
        mediumSource += max(light, vec3(0)) * VgeWaterPhase(dot(-direction, V), medium.anisotropy);
        refractedSource += max(light, vec3(0)) * VgeWaterPhase(dot(-direction, waterOutgoing), medium.anisotropy);
        reflected += cookTorranceBRDF(N,V,direction,F0,roughness) * light * max(dot(N,direction),0.0);
        diffuse += tint * light * max(dot(N,direction),0.0);
    }
#endif
    // Environment reflection is intentionally local and bounded: no active framebuffer feedback or SSR holes.
    reflected += max(vge_atmosphereEnvironment, vec3(0)) * vge_skyVisibility * fresnel * (1.0 - .5 * roughness);
    float alpha = 1.0;
    vec3 radiance;
    vec3 refractedRadiance = vec3(0);
    if (water)
    {
        if (receiver.valid)
        {
            vec3 refractedBackground = receiver.radiance;
            if (underwater)
            {
                // The outgoing segment is air; the camera-to-interface segment remains submerged.
                refractedBackground = VgeApplyAerial(refractedBackground,
                    toWorld * (receiver.positionVS - vge_viewPosition), vge_skyVisibility,
                    vge_atmosphereAerialParams.xy, vge_atmosphereSunDirection);
            }
            vec3 transmittedBackground = VgeWaterTransport(medium, receiver.submergedLength,
                refractedBackground, refractedSource);
            refractedRadiance = reflected + (1.0 - fresnel) * clamp(material.a, 0.0, 1.0) * transmittedBackground;
            if (!underwater)
                refractedRadiance = VgeApplyAerial(refractedRadiance, toWorld * vge_viewPosition, vge_skyVisibility,
                    vge_atmosphereAerialParams.xy, vge_atmosphereSunDirection);
        }
        vec2 screenUv = clamp(gl_FragCoord.xy / frameSize, vec2(0), vec2(1));
        float thickness = liquidMediumControl.y > .5 ? 0.0
            : VgeLiquidThickness(texture(depthTex, screenUv).r, gl_FragCoord.z, vge_viewPosition, underwater);
        vec3 transmittance = VgeWaterTransmittance(medium, thickness);
        float transmitted = dot(transmittance, vec3(.2126,.7152,.0722)) * clamp(material.a,0.0,1.0);
        alpha = clamp(1.0 - (1.0 - fresnel) * transmitted, .001, 1.0);
        vec3 scattering = VgeWaterInScattering(medium, thickness, mediumSource) * (1.0 - fresnel);
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
    // Fade premultiplied color and coverage together. The residual original background
    // is exactly (1-confidence)*(1-fallbackAlpha), rather than adding it to a complete refracted source.
    vec4 combined = VgeWaterCompose(radiance, alpha, refractedRadiance, receiver.confidence);
    // Display adaptation is an output boundary, never part of the optical interpolation.
    return vec4(VgeSceneOutput(combined.rgb, gl_FragCoord.xy, liquidMediumControl.w > .5), combined.a);
}
#endif
