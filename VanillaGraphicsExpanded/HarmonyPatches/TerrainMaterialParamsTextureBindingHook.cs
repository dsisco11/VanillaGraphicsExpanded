using VanillaGraphicsExpanded.Rendering;
using OpenTK.Graphics.OpenGL;

using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.ModSystems;

using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Binds per-atlas material and normal resources at terrain renderer call sites.</summary>
internal static class TerrainMaterialParamsTextureBindingHook
{
    private const int NormalDepthTextureUnit = 14;
    private const int MaterialTextureUnit = 15;

    /// <summary>
    /// Uniform name for the material params sampler we inject into patched shaders.
    /// </summary>
    private const string MaterialSamplerUniform = "vge_materialParamsTex";

    /// <summary>
    /// Uniform name for the normal+depth sampler we inject into patched chunk shaders.
    /// </summary>
    private const string NormalDepthSamplerUniform = "vge_normalDepthTex";

    /// <summary>
    /// Cached uniform location per shader program id (-1 means "not present" or not queried yet).
    /// </summary>
    private static readonly Dictionary<int, int> uniformLocationCache = new();

    /// <summary>
    /// Cached uniform location per shader program id for the normal+depth sampler.
    /// </summary>
    private static readonly Dictionary<int, int> normalDepthUniformLocationCache = new();


    private static int lastBoundNormalDepthTexId;
    private static int lastBoundAtlasTexId;

    /// <summary>Reports the last atlas pair for existing material debug overlays.</summary>
    public static bool TryGetLastBoundNormalDepthTextureId(out int normalDepthTextureId, out int baseAtlasTextureId)
    {
        normalDepthTextureId = lastBoundNormalDepthTexId;
        baseAtlasTextureId = lastBoundAtlasTexId;
        return normalDepthTextureId != 0;
    }

    /// <summary>Binds material and relief resources for the atlas selected by an engine terrain draw.</summary>
    internal static void BindAtlas(ShaderProgramBase __instance, int value)
    {
        // Relief has an explicit linked-program allowlist and must reset missing pages too.
        TerrainReliefBindings.Bind(__instance, value, MaterialAtlasSystem.Instance.TextureStore);
        // Early-out if atlas texture id is invalid.
        if (value == 0)
        {
            return;
        }

        // Early-out if shader program is not yet compiled or is being disposed.
        int programId = __instance.ProgramId;
        if (programId == 0)
        {
            return;
        }

        if (!MaterialAtlasSystem.Instance.IsInitialized)
        {
            return;
        }

        MaterialAtlasTextureStore store = MaterialAtlasSystem.Instance.TextureStore;
        bool hasMaterialParams = store.TryGetMaterialParamsTextureId(value, out int materialTexId);
        bool hasNormalDepth = store.TryGetNormalDepthTextureId(value, out int normalDepthTexId);

        if (!hasMaterialParams && !hasNormalDepth)
        {
            return;
        }

        try
        {
            if (hasMaterialParams)
            {
                if (!uniformLocationCache.TryGetValue(programId, out int materialUniformLoc))
                {
                    materialUniformLoc = GL.GetUniformLocation(programId, MaterialSamplerUniform);
                    uniformLocationCache[programId] = materialUniformLoc;
                }

                if (materialUniformLoc >= 0)
                {
                    StateCache.Current.ActiveTexture(MaterialTextureUnit);
                    StateCache.Current.BindTextureOnActiveUnit(TextureTarget.Texture2D, materialTexId);
                    GL.Uniform1(materialUniformLoc, MaterialTextureUnit);
                }
            }

            if (hasNormalDepth)
            {
                // Capture for debug overlays (e.g., showing the currently active atlas page).
                lastBoundNormalDepthTexId = normalDepthTexId;
                lastBoundAtlasTexId = value;

                if (!normalDepthUniformLocationCache.TryGetValue(programId, out int normalDepthUniformLoc))
                {
                    normalDepthUniformLoc = GL.GetUniformLocation(programId, NormalDepthSamplerUniform);
                    normalDepthUniformLocationCache[programId] = normalDepthUniformLoc;
                }

                if (normalDepthUniformLoc >= 0)
                {
                    StateCache.Current.ActiveTexture(NormalDepthTextureUnit);
                    StateCache.Current.BindTextureOnActiveUnit(TextureTarget.Texture2D, normalDepthTexId);
                    GL.Uniform1(normalDepthUniformLoc, NormalDepthTextureUnit);
                }
            }
        }
        catch
        {
            // Swallow GL errors during early init / shutdown to avoid crashing the game.
        }
    }

    /// <summary>
    /// Call when shaders are reloaded to clear cached uniform locations.
    /// </summary>
    public static void ClearUniformCache()
    {
        TerrainReliefBindings.Reset();
        uniformLocationCache.Clear();
        normalDepthUniformLocationCache.Clear();
    }
}
