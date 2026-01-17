using HarmonyLib;

using OpenTK.Graphics.OpenGL;

using System;
using System.Collections.Generic;
using System.Reflection;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.ModSystems;

using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>
/// Binds VGE's per-atlas material params texture whenever the engine sets a TerrainTex property on chunk shaders.
/// We patch the property setters (set_Tex2dTerrain, set_Tex2dTerrainLinear) on concrete shader classes
/// because patching BindTexture2D is ambiguous (multiple overloads) and not reliably available.
/// </summary>
internal static class TerrainMaterialParamsTextureBindingHook
{
    private const int NormalDepthTextureUnit = 14;
    private const int MaterialTextureUnit = 15;

    /// <summary>
    /// Chunk shader class names and the property names that set the block atlas texture.
    /// We attempt to patch all combinations; missing types/properties are logged and skipped.
    /// </summary>
    private static readonly (string TypeName, string PropertyName)[] TargetProperties =
    {
        ("Vintagestory.Client.NoObf.ShaderProgramChunkopaque", "TerrainTex2D"),
        ("Vintagestory.Client.NoObf.ShaderProgramChunkopaque", "TerrainTex2DLinear"),
        ("Vintagestory.Client.NoObf.ShaderProgramChunktopsoil", "TerrainTex2D"),
        ("Vintagestory.Client.NoObf.ShaderProgramChunktopsoil", "TerrainTex2DLinear"),
        ("Vintagestory.Client.NoObf.ShaderProgramChunkliquid", "TerrainTex2D"),
        ("Vintagestory.Client.NoObf.ShaderProgramChunktransparent", "TerrainTex2D"),
    };

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

    private static Action<string>? runtimeLog;

    private static int lastBoundNormalDepthTexId;
    private static int lastBoundAtlasTexId;

    public static bool TryGetLastBoundNormalDepthTextureId(out int normalDepthTextureId, out int baseAtlasTextureId)
    {
        normalDepthTextureId = lastBoundNormalDepthTexId;
        baseAtlasTextureId = lastBoundAtlasTexId;
        return normalDepthTextureId != 0;
    }

    /// <summary>
    /// Called from VgeModSystem to apply patches manually via Harmony.
    /// </summary>
    public static void ApplyPatches(Harmony harmony, Action<string> log)
    {
        runtimeLog = log;

        var postfix = new HarmonyMethod(typeof(TerrainMaterialParamsTextureBindingHook), nameof(SetTex2dTerrain_Postfix));
        int patchedCount = 0;

        foreach ((string typeName, string propertyName) in TargetProperties)
        {
            Type? type = AccessTools.TypeByName(typeName);
            if (type is null)
            {
                log($"[VGE] TerrainMaterialParamsTextureBindingHook: type not found: {typeName}");
                continue;
            }

            MethodInfo? setter = AccessTools.PropertySetter(type, propertyName);
            if (setter is null)
            {
                log($"[VGE] TerrainMaterialParamsTextureBindingHook: property setter not found: {typeName}.{propertyName}");
                continue;
            }

            try
            {
                harmony.Patch(setter, postfix: postfix);
                patchedCount++;
                log($"[VGE] Patched {typeName}.set_{propertyName}");
            }
            catch (Exception ex)
            {
                log($"[VGE] Failed to patch {typeName}.set_{propertyName}: {ex.Message}");
            }
        }

        log($"[VGE] TerrainMaterialParamsTextureBindingHook: {patchedCount}/{TargetProperties.Length} property setters patched.");
    }

    /// <summary>
    /// Postfix for Tex2dTerrain / Tex2dTerrainLinear setters.
    /// The property setter signature is typically `set_Tex2dTerrain(int value)` where value is the GL texture id.
    /// __instance is the ShaderProgramBase-derived shader, value is the atlas texture id just bound.
    /// </summary>
    public static void SetTex2dTerrain_Postfix(ShaderProgramBase __instance, int value)
    {
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

        if (!PbrMaterialAtlasTextures.Instance.IsInitialized)
        {
            return;
        }

        bool hasMaterialParams = PbrMaterialAtlasTextures.Instance.TryGetMaterialParamsTextureId(value, out int materialTexId);
        bool hasNormalDepth = PbrMaterialAtlasTextures.Instance.TryGetNormalDepthTextureId(value, out int normalDepthTexId);

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
                    GL.ActiveTexture(TextureUnit.Texture0 + MaterialTextureUnit);
                    GL.BindTexture(TextureTarget.Texture2D, materialTexId);
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
                    GL.ActiveTexture(TextureUnit.Texture0 + NormalDepthTextureUnit);
                    GL.BindTexture(TextureTarget.Texture2D, normalDepthTexId);
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
        uniformLocationCache.Clear();
        normalDepthUniformLocationCache.Clear();
    }
}

