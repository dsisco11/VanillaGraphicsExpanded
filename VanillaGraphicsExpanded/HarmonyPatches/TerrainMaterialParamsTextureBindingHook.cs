using HarmonyLib;

using OpenTK.Graphics.OpenGL;

using System;
using System.Collections.Generic;
using System.Reflection;

using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>
/// Binds VGE's per-atlas material params texture whenever the engine sets a TerrainTex property on chunk shaders.
/// We patch the property setters (set_Tex2dTerrain, set_Tex2dTerrainLinear) on concrete shader classes
/// because patching BindTexture2D is ambiguous (multiple overloads) and not reliably available.
/// </summary>
internal static class TerrainMaterialParamsTextureBindingHook
{
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
    /// Cached uniform location per shader program id (-1 means "not present" or not queried yet).
    /// </summary>
    private static readonly Dictionary<int, int> uniformLocationCache = new();

    /// <summary>
    /// Called from VgeModSystem to apply patches manually via Harmony.
    /// </summary>
    public static void ApplyPatches(Harmony harmony, Action<string> log)
    {
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

        if (!PbrMaterialAtlasTextures.Instance.TryGetMaterialParamsTextureId(value, out int materialTexId))
        {
            return;
        }

        try
        {
            if (!uniformLocationCache.TryGetValue(programId, out int uniformLoc))
            {
                uniformLoc = GL.GetUniformLocation(programId, MaterialSamplerUniform);
                uniformLocationCache[programId] = uniformLoc;
            }

            if (uniformLoc < 0)
            {
                // Shader doesn't have the uniform (not patched or stripped).
                return;
            }

            // Bind our material params texture to the dedicated unit and set the sampler uniform.
            GL.ActiveTexture(TextureUnit.Texture0 + MaterialTextureUnit);
            GL.BindTexture(TextureTarget.Texture2D, materialTexId);
            GL.Uniform1(uniformLoc, MaterialTextureUnit);
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
    }
}

