using HarmonyLib;

using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>
/// Binds VGE's per-atlas material params texture whenever the engine binds the block atlas textures.
/// </summary>
[HarmonyPatch(typeof(ShaderProgram), nameof(ShaderProgram.BindTexture2D))]
internal static class TerrainMaterialParamsTextureBindingHook
{
    private const string TerrainSampler0 = "terrainTex";
    private const string TerrainSampler1 = "terrainTexLinear";
    private const string MaterialSampler = "vge_materialParamsTex";
    private const int MaterialTextureUnit = 15;

    private static int _reentryGuard;

    [HarmonyPostfix]
    public static void BindTexture2D_Postfix(ShaderProgram __instance, string samplerName, int textureId, int textureNumber)
    {
        // Avoid recursion if we call BindTexture2D ourselves.
        if (samplerName == MaterialSampler)
        {
            return;
        }

        // Only react when vanilla binds block atlas textures.
        if (samplerName != TerrainSampler0 && samplerName != TerrainSampler1)
        {
            return;
        }

        // Only bind if the shader actually has the sampler.
        if (!__instance.HasUniform(MaterialSampler))
        {
            return;
        }

        if (textureId == 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _reentryGuard, 1) == 1)
        {
            return;
        }

        try
        {
            if (PbrMaterialAtlasTextures.Instance.TryGetMaterialParamsTextureId(textureId, out int materialTexId))
            {
                __instance.BindTexture2D(MaterialSampler, materialTexId, MaterialTextureUnit);
            }
        }
        finally
        {
            Volatile.Write(ref _reentryGuard, 0);
        }
    }
}
