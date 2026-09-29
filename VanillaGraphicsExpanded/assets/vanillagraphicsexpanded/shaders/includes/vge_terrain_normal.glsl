#ifndef VGE_TERRAIN_NORMAL_GLSL
#define VGE_TERRAIN_NORMAL_GLSL

uniform int vge_twoSidedTerrain;

/** Corrects legacy upward lighting normals on two-sided terrain and selects the visible hemisphere. */
vec3 VgeTerrainNormal(vec3 authoredNormal, vec3 position)
{
    vec3 n = normalize(authoredNormal);
    // Evaluate derivatives before any alpha discard. Their cross product faces the viewing side.
    vec3 face = cross(dFdx(position), dFdy(position));
    float lengthSquared = dot(face, face);
    if (vge_twoSidedTerrain == 0) return n;
    bool validFace = !any(isnan(face)) && !any(isinf(face)) && lengthSquared > 1e-16;
    if (validFace)
    {
        face *= inversesqrt(lengthSquared);
        // Cross plants and unshaded shape elements encode UP instead of their actual face normal.
        // Preserve other authored normals, including deliberate smoothing, on two-sided meshes.
        if (n.y > 0.999) n = face;
        else if (dot(n, face) < 0.0) n = -n;
    }
    else if (!gl_FrontFacing) n = -n;
    return n;
}
#endif
