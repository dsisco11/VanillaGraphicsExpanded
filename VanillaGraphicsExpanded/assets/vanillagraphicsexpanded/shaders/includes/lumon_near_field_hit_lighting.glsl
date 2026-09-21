#ifndef LUMON_NEAR_FIELD_HIT_LIGHTING_GLSL
#define LUMON_NEAR_FIELD_HIT_LIGHTING_GLSL
@import "./lumon_near_field_trace.glsl"

/** Evaluates the existing normalized voxel-light approximation with hit-face materials and bounded sky visibility. */
bool lumonShadeNearFieldHit(LumonNearFieldHit hit, float traceDistance, float emissionBoost, out vec3 radiance)
{
    radiance = vec3(0.0);
    if (hit.material == 0u) return false;
    int face = hit.normal.x > 0 ? 1 : hit.normal.x < 0 ? 3 :
        hit.normal.y > 0 ? 4 : hit.normal.y < 0 ? 5 : hit.normal.z > 0 ? 2 : 0;
    int materialTexel = int(hit.material) * 12 + face * 2;
    vec4 material = texelFetch(nearFieldMaterials, ivec2(materialTexel % 256, materialTexel / 256), 0);
    int emissionTexel = materialTexel + 1;
    vec3 emission = texelFetch(nearFieldMaterials, ivec2(emissionTexel % 256, emissionTexel / 256), 0).rgb;
    uint outsideGeometry; vec4 outsideLight;
    if (!lumonNearFieldRead(hit.cell + hit.normal, outsideGeometry, outsideLight)) return false;
    float skyFactor = 0.0;
    if (outsideLight.a > 1e-6)
    {
        vec3 start = hit.fraction + vec3(hit.normal) * 0.001;
        ivec3 offset = ivec3(floor(start));
        for (int sampleIndex = 0; sampleIndex < 2; sampleIndex++)
        {
            float y = (float(sampleIndex) + 0.5) / 2.0;
            float radius = sqrt(1.0 - y * y);
            float phi = float(sampleIndex) * 2.39996323;
            vec3 skyDirection = vec3(radius * cos(phi), y, radius * sin(phi));
            float weight = max(dot(vec3(hit.normal), skyDirection), 0.0);
            if (weight > 0.0 && lumonTraceNearField(hit.cell + offset, fract(start), skyDirection, min(traceDistance, 16.0)).outcome == LUMON_NEAR_FIELD_CLEAR)
                skyFactor += weight;
        }
    }
    radiance = clamp(outsideLight.rgb, 0.0, 1.0) +
        material.rgb * (clamp(outsideLight.a, 0.0, 1.0) * clamp(skyFactor, 0.0, 1.0) / LUMON_PI) +
        max(emission, vec3(0.0)) * emissionBoost;
    return true;
}
#endif
