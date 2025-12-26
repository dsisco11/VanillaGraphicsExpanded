using System;
using System.Collections.Generic;
using System.IO;

using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

using HarmonyInstance = global::HarmonyLib.Harmony;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Harmony patches to inject G-buffer normal output into chunk shaders.
/// </summary>
public static class ShaderPatches
{
    #region Constants and Fields
    private const string HarmonyId = "vanillagraphicsexpanded.shaderpatches";
    private const string ModDomain = "vanillagraphicsexpanded";
    
    private static HarmonyInstance? harmony;
    private static ICoreAPI? _api;

    /// <summary>
    /// Tracks which shaders have already been patched to avoid double injection.
    /// </summary>
    public static HashSet<string> AlreadyPatchedShaders = [];
    /// <summary>
    /// Cache for included shader code snippets to avoid redundant loading.
    /// </summary>
    public static Dictionary<string, string> IncludesCache = [];
    #endregion

#region G-Buffer Injection Code

    // G-Buffer output declarations (locations 4-6, after VS's 0-3)
    // Location 4: World-space normals (RGBA16F)
    // Location 5: Material properties (RGBA16F) - Reflectivity, Roughness, Metallic, Emissive
    // Location 6: Albedo (RGB8) - Base color without lighting
    private const string GBufferOutputDeclarations = @"
// VGE G-Buffer outputs
layout(location = 4) out vec4 vge_outNormal;    // World-space normal (XYZ), unused (W)
layout(location = 5) out vec4 vge_outMaterial;  // Reflectivity, Roughness, Metallic, Emissive
// layout(location = 6) out vec4 vge_outAlbedo;    // Base color RGB, Alpha
";

    // Code to inject before the final closing brace of main() to write G-buffer data
    // Uses available shader variables: normal, renderFlags, texColor/rgba
    private const string GBufferOutputWrites = @"
    // VGE: Write G-buffer outputs
    // Normal: world-space normal packed to [0,1] range
    vge_outNormal = vec4(normal * 0.5 + 0.5, 1.0);
    
    // Material: extract properties from renderFlags
    float vge_reflectivity = ((renderFlags & ReflectiveBitMask) != 0) ? 1.0 : 0.0;
    float vge_roughness = 0.5;  // Default roughness (could be extracted from texture)
    float vge_metallic = 0.0;   // Default non-metallic
    float vge_emissive = glowLevel;
    vge_outMaterial = vec4(vge_reflectivity, vge_roughness, vge_metallic, vge_emissive);
    
    // Albedo: base texture color before lighting
    // vge_outAlbedo = texColor;
";
#endregion

    public static void Apply(ICoreAPI api)
    {
        _api = api;
        harmony = new HarmonyInstance(HarmonyId);
        harmony.PatchAll();
        api.Logger.Notification("[VGE] Harmony shader patches applied");
    }

    public static void Unpatch(ICoreAPI? api)
    {
        harmony?.UnpatchAll(HarmonyId);
        _api = null;
        api?.Logger.Notification("[VGE] Harmony shader patches removed");
    }

    /// <summary>
    /// Loads all shader include files from the mod's shaderincludes asset folder.
    /// Should be called from the ModSystem's AssetsLoaded hook.
    /// </summary>
    /// <param name="api">The core API instance.</param>
    public static void LoadShaderIncludes(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Client)
        {
            return;
        }

        var assetManager = api.Assets;
        var shaderIncludeAssets = assetManager.GetManyInCategory(
            AssetCategory.shaderincludes.Code,
            pathBegins: "",
            domain: ModDomain,
            loadAsset: true);

        if (shaderIncludeAssets.Count == 0)
        {
            api.Logger.Debug("[VGE] No shader includes found in mod assets");
            return;
        }

        RegisterShaderProgramPatch.IncludesCache.Clear();

        foreach (var asset in shaderIncludeAssets)
        {
            var fileName = Path.GetFileName(asset.Location.Path);
            var code = asset.ToText();

            if (string.IsNullOrEmpty(code))
            {
                api.Logger.Warning($"[VGE] Shader include '{fileName}' is empty or failed to load");
                continue;
            }

            RegisterShaderProgramPatch.IncludesCache[fileName] = code;
            api.Logger.Debug($"[VGE] Loaded shader include: {fileName} ({code.Length} chars)");
        }

        api.Logger.Notification($"[VGE] Loaded {RegisterShaderProgramPatch.IncludesCache.Count} shader include(s) from mod assets");
    }

    /// <summary>
    /// Patches ShaderRegistry.RegisterShaderProgram to intercept chunk shader compilation.
    /// </summary>
    [HarmonyPatch]
    public static class RegisterShaderProgramPatch
    {
        /// <summary>
        /// Shaders to patch for G-buffer output injection.
        /// </summary>
        public static HashSet<string> TargetShaders = ["chunkopaque", "chunktopsoil", "standard", "instanced"];
        [HarmonyPatch(typeof(Vintagestory.Client.NoObf.ShaderRegistry), "LoadShaderProgram")]
        [HarmonyPostfix]
        static void LoadShaderProgram(Vintagestory.Client.NoObf.ShaderProgram program, bool useSSBOs)
        {
            // Only inject into chunk shaders
            string shaderName = program.PassName;
            if (!TargetShaders.Contains(shaderName))
            {
                return;
            }

            try
            {
                var fragmentShader = program?.FragmentShader;
                if (fragmentShader == null) return;

                string? code = fragmentShader.Code;
                if (string.IsNullOrEmpty(code)) return;

                // Don't inject twice
                if (AlreadyPatchedShaders.Contains(shaderName))
                {
                    return;
                }

                // Use the fluent shader patcher to inject G-buffer outputs
                code = new ShaderSourcePatcher(code, shaderName)
                    .AfterVersionDirective().Insert(GBufferOutputDeclarations)
                    .BeforeMainClose().Insert(GBufferOutputWrites)
                    .Build();

                fragmentShader.Code = code;
                AlreadyPatchedShaders.Add(shaderName);
                _api?.Logger.Debug($"[VGE] Injected G-buffer outputs into {shaderName}");
            }
            catch (ShaderPatchException ex)
            {
                _api?.Logger.Warning($"[VGE] Failed to patch {shaderName}: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Log but don't crash
                _api?.Logger.Error($"[VGE] Unexpected error patching {shaderName}: {ex.Message}");
            }
        }
    }
}
