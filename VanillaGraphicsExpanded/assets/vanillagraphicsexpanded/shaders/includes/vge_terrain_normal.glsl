#ifndef VGE_TERRAIN_NORMAL_GLSL
#define VGE_TERRAIN_NORMAL_GLSL

#ifndef VGE_TERRAIN_NORMAL_INPUTS
uniform int vge_twoSidedTerrain;
#endif

/** Uses geometry for transmitting surfaces and corrects legacy two-sided lighting normals. */
vec3 VgeTerrainNormal(vec3 authoredNormal, vec3 position, float transmission)
{
    vec3 n = normalize(authoredNormal);
    // Evaluate derivatives before any alpha discard. Their cross product faces the viewing side.
    vec3 face = cross(dFdx(position), dFdy(position));
    float lengthSquared = dot(face, face);
    bool geometric = transmission > 0.0;
    if (vge_twoSidedTerrain == 0 && !geometric) return n;
    bool validFace = !any(isnan(face)) && !any(isinf(face)) && lengthSquared > 1e-16;
    if (validFace)
    {
        face *= inversesqrt(lengthSquared);
        // Cross plants and unshaded shape elements encode UP instead of their actual face normal.
        // Transmitting materials always use geometry, including gradient-shaded or sheltered leaves.
        // Preserve other authored normals only for non-transmitting materials.
        if (geometric || n.y > 0.999) n = face;
        else if (dot(n, face) < 0.0) n = -n;
    }
    else if (!gl_FrontFacing) n = -n;
    return n;
}
#endif
