#ifndef VGE_PBR_LIQUID_GLSL
#define VGE_PBR_LIQUID_GLSL
@import "./pbr_scene_storage.glsl"
@import "./pbr_finite_disk_brdf.glsl"
@import "./pbr_photometric.glsl"
@import "./pbr_microfacet_energy.glsl"
@import "./shadows/surface.glsl"
@import "./pbr_liquid_optics.glsl"
@import "./liquids/medium_material.glsl"
@import "./liquids/refraction_selection.glsl"
@import "./liquids/celestial_lighting.glsl"

/** Evaluates a liquid interface and a bounded homogeneous medium using existing OIT transmission. */
vec4 VgeLiquidSurface(vec4 textureColor, vec4 material, bool lava, bool fullAlpha)
{
    mat3 toWorld = mat3(vgeFrame.invViewMatrix);
    vec3 V = normalize(toWorld * -vge_viewPosition);
    vec3 receiverPosition = (vgeFrame.invViewMatrix * vec4(vge_viewPosition, 1)).xyz;
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
    // Wave and mesh normals are authored in the pool's local frame. The view
    // reconstruction already includes its model transform, so convert the normal too.
    mat3 normalToWorld = transpose(inverse(mat3(modelMatrix)));
    N = normalize(normalToWorld * N);
    if (dot(N, V) < 0.0) N = -N;
    vec3 tint = clamp(VgeSrgbToLinear(textureColor.rgb), vec3(0), vec3(1));
    float roughness = clamp(material.r, .04, 1.0);
    bool underwater = cameraUnderwater > .7;
    float cosine = clamp(dot(N, V), 0.0, 1.0);
    float metallic = water ? 0.0 : clamp(material.g,0.0,1.0);
    vec3 F0 = water ? vec3(.02037) : mix(vec3(.04),tint,metallic);
    VgeLobeEnergy energy = VgeEvaluateMicrofacetEnergy(F0,roughness,cosine);
    if(!energy.valid) return vec4(0);
    vec3 surfaceBudget = vec3(1)-energy.reflectance;
    // Retain exact underwater total internal reflection; rough-interface transmission uses the shared lobe budget.
    float fresnel = energy.reflectance.r;
    if(water && underwater && VgeLiquidFresnel(cosine,true)>=1.0) { fresnel=1.0; surfaceBudget=vec3(0); }
    VgeLiquidCelestialLight sun=VgeEvaluateLiquidCelestial(0,vgeSurfaceSunDirection,vgeSurfaceSolar,
        vgeSurfaceSunRadius,vge_skyVisibility,receiverPosition,N,V,F0,roughness,energy.compensation);
    VgeLiquidCelestialLight moon=VgeEvaluateLiquidCelestial(1,vgeSurfaceMoonDirection,vgeSurfaceLunar,
        vgeSurfaceMoonRadius,vge_skyVisibility,receiverPosition,N,V,F0,roughness,energy.compensation);
    vec3 reflected=sun.reflection+moon.reflection;
#if VGE_LIQUID_CAPTURE_MODE == 1
    // Capture both direct celestial reflections before medium and OIT composition.
    return vec4(liquidMediumControl.w > .5 ? VgeStoreSceneLighting(reflected)
        : VgeSceneOutput(reflected, gl_FragCoord.xy, false), 1.0);
#endif
    vec3 bodyLight = VgeDiffuseIncident(vge_blockIrradiance) + vge_atmosphereEnvironment * vge_skyVisibility;
    VgeWaterMedium medium = water ? VgeWaterMaterial(uv) : VgeWaterMedium(vec3(0), vec3(0), 0.0);
    VgeWaterReceiver receiver = VgeWaterReceiver(false, VGE_WATER_RECEIVER_NONE,
        vec3(0), vge_viewPosition, vec3(0), 0.0, 0.0);
    if (water && liquidMediumControl.z > .5 && fresnel < 1.0)
    {
        vec3 normalVS = normalize(mat3(vgeFrame.viewMatrix) * N);
        // The mesh normal excludes wave detail and supplies the neutral PNO surface.
        vec3 baseNormal = normalize(normalToWorld * fragNormal);
        if (dot(baseNormal, V) < 0.0) baseNormal = -baseNormal;
        vec3 baseNormalVS = normalize(mat3(vgeFrame.viewMatrix) * baseNormal);
        receiver = VgeWaterSelectRefraction(vge_viewPosition, normalVS, baseNormalVS, underwater);
    }
    // Only a valid, fully weighted receiver replaces the ordinary contribution.
    // Partial confidence keeps both paths; shared reflection lighting remains outside this gate.
    bool needsFallback = !receiver.valid || !(receiver.confidence >= 1.0);
    bool scatters = water && any(greaterThan(medium.scattering, vec3(0)));
    bool fallbackSourceNeeded = scatters && needsFallback;
    bool refractedSourceNeeded = scatters && receiver.valid;
    vec3 waterOutgoing = V;
    // Receiver-local shadow/sky visibility bounds these water source approximations.
    // Outgoing photons travel toward V; each celestial source retains its own direction.
    vec3 mediumSource = vec3(0);
    vec3 refractedSource = vec3(0);
    if (fallbackSourceNeeded) mediumSource = max(bodyLight, vec3(0)) * .25;
    if (refractedSourceNeeded)
    {
        // This outgoing direction serves scattering only; reflection retains V.
        if (medium.anisotropy != 0.0)
            waterOutgoing = VgeWaterOutgoingDirection(vge_viewPosition, receiver.refractedDirectionVS, underwater, toWorld);
        refractedSource = max(bodyLight, vec3(0)) * .25;
    }
    vec3 diffuse = vec3(0);
    VgeAccumulateLiquidCelestial(sun,N,V,waterOutgoing,fallbackSourceNeeded,refractedSourceNeeded,
        medium.anisotropy,surfaceBudget,metallic,tint,mediumSource,refractedSource,diffuse);
    VgeAccumulateLiquidCelestial(moon,N,V,waterOutgoing,fallbackSourceNeeded,refractedSourceNeeded,
        medium.anisotropy,surfaceBudget,metallic,tint,mediumSource,refractedSource,diffuse);
    for (int i = 0; i < clamp(pointLightQuantity, 0, vgeLights.positions.length()); ++i)
    {
        vec3 delta = vgeLights.positions[i].xyz - vge_viewPosition;
        float d2 = max(dot(delta, delta), .0001);
        vec3 direction = toWorld * delta;
        direction *= inversesqrt(max(dot(direction, direction), .0001));
        vec3 light = VgePointIlluminance(vgeLights.colors[i], d2)
            * vgeSurfaceShadowVisibility(2 + i, receiverPosition, N);
        if (fallbackSourceNeeded)
            mediumSource += max(light, vec3(0)) * VgeWaterPhase(-direction, V, medium.anisotropy);
        if (refractedSourceNeeded)
            refractedSource += max(light, vec3(0)) * VgeWaterPhase(-direction, waterOutgoing, medium.anisotropy);
        reflected += cookTorranceBRDF(N,V,direction,F0,roughness)*energy.compensation*light*max(dot(N,direction),0.0);
        diffuse += surfaceBudget*(1.0-metallic)*tint*VgeDiffuseIncident(light)*max(dot(N,direction),0.0);
    }
    // Environment reflection is intentionally local and bounded: no active framebuffer feedback or SSR holes.
    vec3 environmentReflectance = water && fresnel>=1.0 ? vec3(1) : energy.reflectance;
    reflected += max(vge_atmosphereEnvironment,vec3(0))*vge_skyVisibility*environmentReflectance;
    float alpha = 1.0;
    vec3 radiance = vec3(0);
    vec3 refractedRadiance = vec3(0);
    if (water)
    {
        if (receiver.valid)
        {
            vec3 refractedBackground = liquidMediumControl.w > .5
                ? VgePhysicalFrameLight(receiver.radiance) : receiver.radiance;
            if (underwater)
            {
                // The outgoing segment is air; the camera-to-interface segment remains submerged.
                refractedBackground = VgeApplyAerial(refractedBackground,
                    toWorld * (receiver.positionVS - vge_viewPosition), vge_skyVisibility,
                    vge_atmosphereAerialParams.xyw, vgeSurfaceSunDirection);
            }
            vec3 transmittedBackground = VgeWaterTransport(medium, receiver.submergedLength,
                refractedBackground, refractedSource);
            refractedRadiance = reflected + (1.0 - fresnel) * clamp(material.a, 0.0, 1.0) * transmittedBackground;
        }
        if (needsFallback)
        {
            vec2 screenUv = clamp(gl_FragCoord.xy / frameSize, vec2(0), vec2(1));
            float thickness = liquidMediumControl.y > .5 ? 0.0
                : VgeLiquidThickness(texture(depthTex, screenUv).r, gl_FragCoord.z, vge_viewPosition, underwater);
            VgeWaterPath path = VgeWaterEvaluatePath(medium, thickness);
            float transmitted = dot(path.transmittance, vec3(.2126,.7152,.0722)) * clamp(material.a,0.0,1.0);
            alpha = clamp(1.0 - (1.0 - fresnel) * transmitted, .001, 1.0);
            vec3 scattering = VgeWaterInScattering(medium, path, mediumSource) * (1.0 - fresnel);
            // OIT multiplies by alpha. Normalize only our surface source, not the background transmission.
            radiance = (reflected + scattering) / alpha;
        }
    }
    else
    {
        // Milk/dyes/oil and emissive lava retain distinct opaque body response; no water absorption is inferred.
        radiance = reflected + diffuse + surfaceBudget*(1.0-metallic)*tint*bodyLight + VgeEmissionLuminance(tint, emission);
    }
    // Fade premultiplied color and coverage together. The residual original background
    // is exactly (1-confidence)*(1-fallbackAlpha), rather than adding it to a complete refracted source.
    vec4 combined = VgeWaterCompose(radiance, alpha, refractedRadiance, receiver.confidence);
    // The shared camera segment is affine in radiance (T * color + S).
    // Composing coverage first weights S by the combined alpha exactly once,
    // including partial-confidence fallback. Underwater exit air stays above.
    if (!underwater)
        combined.rgb = VgeApplyAerial(combined.rgb, toWorld * vge_viewPosition, vge_skyVisibility,
            vge_atmosphereAerialParams.xyw, vgeSurfaceSunDirection);
    // Display adaptation is an output boundary, never part of the optical interpolation.
    return vec4(liquidMediumControl.w > .5 ? VgeStoreSceneLighting(combined.rgb)
        : VgeSceneOutput(combined.rgb, gl_FragCoord.xy, false), combined.a);
}
#endif
