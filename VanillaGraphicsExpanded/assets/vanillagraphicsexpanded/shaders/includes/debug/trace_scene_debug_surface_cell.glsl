#ifndef LUMON_DEBUG_TRACE_SCENE_DEBUG_SURFACE_CELL_GLSL
#define LUMON_DEBUG_TRACE_SCENE_DEBUG_SURFACE_CELL_GLSL


/** Implements trace scene debug surface cell for its explicit view entrypoint. */
ivec3 traceSceneDebugSurfaceCell(vec2 screenPos, float depth)
{
    vec3 view = lumonReconstructViewPos(screenPos / screenSize, depth, invProjectionMatrix);
    vec3 relative = (invViewMatrix * vec4(view, 1.0)).xyz;
    uvec4 surfacePatch = texelFetch(gBufferPatchId, ivec2(screenPos), 0);
    vec3 normal = texelFetch(gBufferSurface, ivec3(ivec2(screenPos), VGE_SURFACE_NORMAL), 0).xyz * 2.0 - 1.0;
    if (surfacePatch.y != 0u)
    {
        uint axis = (surfacePatch.y - 1u) % 6u;
        normal = vec3(0.0); normal[int(axis / 2u)] = (axis % 2u) == 0u ? 1.0 : -1.0;
    }
    if (dot(normal, normal) > 1e-6) relative -= normalize(normal) * 0.51;
    return LumonFrameMatrixSpacePosToWorldCell(relative);
}
#endif
