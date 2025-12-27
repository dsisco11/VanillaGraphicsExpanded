using System;
using System.Collections.Generic;
using System.IO;

using HarmonyLib;

using VanillaGraphicsExpanded.Harmony;

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
    /// Cache for imported shader code snippets to avoid redundant loading.
    /// </summary>
    public static Dictionary<string, string> ImportsCache = [];
    #endregion

#region G-Buffer Injection Code

    // G-Buffer output declarations (locations 4-5, after VS's 0-3)
    // Location 4: World-space normals (RGBA16F)
    // Location 5: Material properties (RGBA16F) - Reflectivity, Roughness, Metallic, Emissive
    // Note: VS's ColorAttachment0 (outColor) serves as albedo
    private const string GBufferOutputDeclarations = @"
// VGE G-Buffer outputs
layout(location = 4) out vec4 vge_outNormal;    // World-space normal (XYZ), unused (W)
layout(location = 5) out vec4 vge_outMaterial;  // Reflectivity, Roughness, Metallic, Emissive
";

    // Code to inject before the final closing brace of main() to write G-buffer data
    // Uses available shader variables: normal, renderFlags, texColor/rgba
    // Note: VS's outColor (ColorAttachment0) serves as albedo
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
";
#endregion

    public static void Apply(ICoreAPI api)
    {
        _api = api;
        harmony = new HarmonyInstance(HarmonyId);
        
        // Initialize the shader includes hook with dependencies
        ShaderIncludesHook.Initialize(api.Logger, api.Assets);
        
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
    /// Loads all shader import files from the mod's shaderincludes asset folder.
    /// Should be called from the ModSystem's AssetsLoaded hook.
    /// </summary>
    /// <param name="api">The core API instance.</param>
    public static void LoadShaderImports(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Client)
        {
            return;
        }

        var assetManager = api.Assets;
        var shaderImportAssets = assetManager.GetManyInCategory(
            AssetCategory.shaderincludes.Code,
            pathBegins: "",
            domain: ModDomain,
            loadAsset: true);

        if (shaderImportAssets.Count == 0)
        {
            api.Logger.Debug("[VGE] No shader imports found in mod assets");
            return;
        }

        ImportsCache.Clear();

        foreach (var asset in shaderImportAssets)
        {
            var fileName = Path.GetFileName(asset.Location.Path);
            var code = asset.ToText();

            if (string.IsNullOrEmpty(code))
            {
                api.Logger.Warning($"[VGE] Shader import '{fileName}' is empty or failed to load");
                continue;
            }

            ImportsCache[fileName] = code;
            api.Logger.Debug($"[VGE] Loaded shader import: {fileName} ({code.Length} chars)");
        }

        api.Logger.Notification($"[VGE] Loaded {ImportsCache.Count} shader import(s) from mod assets");
    }

    /// <summary>
    /// Processes shader source code to find and resolve <c>#import</c> directives.
    /// Each import statement is commented out and the referenced file contents are injected below it.
    /// </summary>
    /// <param name="shaderCode">The shader source code to process.</param>
    /// <param name="importsCache">Dictionary mapping import file names to their contents.</param>
    /// <param name="shaderName">Optional shader name for error messages.</param>
    /// <returns>The processed shader code with imports resolved.</returns>
    /// <exception cref="SourceCodePatchException">Thrown if an imported file is not found in the cache.</exception>
    public static string ProcessShaderImports(
        string shaderCode,
        IReadOnlyDictionary<string, string> importsCache,
        string? shaderName = null)
    {
        return SourceCodeImportsProcessor.Process(shaderCode, importsCache, shaderName, _api?.Logger);
    }

    /// <summary>
    /// Patches ShaderRegistry.RegisterShaderProgram to intercept chunk shader compilation.
    /// </summary>
    [HarmonyPatch]
    public static class RegisterShaderProgramPatch
    {
        [HarmonyPatch(typeof(Vintagestory.Client.NoObf.ShaderRegistry), "LoadShaderProgram")]
        [HarmonyPostfix]
        static void LoadShaderProgram(Vintagestory.Client.NoObf.ShaderProgram program, bool useSSBOs)
        {
            string shaderName = program.PassName;
            // Perform import processing for ALL shaders
            try
            {
                if (program.FragmentShader?.Code is not null)
                {
                    var patcher = new SourceCodeImportsProcessor(program.FragmentShader.Code, ImportsCache, shaderName)
                        .ProcessImports(_api?.Logger);

                    // Don't inject twice
                    if (!AlreadyPatchedShaders.Contains(shaderName))
                    {
                        TryInjectGBuffersIntoShader(patcher);
                    }
                    program.FragmentShader.Code = patcher.Build();
                }
                if (program.VertexShader?.Code is not null)
                {
                    program.VertexShader.Code = SourceCodeImportsProcessor.Process(
                        program.VertexShader.Code, ImportsCache, shaderName, _api?.Logger);
                }

                if (program.GeometryShader?.Code is not null)
                {
                    program.GeometryShader.Code = SourceCodeImportsProcessor.Process(
                        program.GeometryShader.Code, ImportsCache, shaderName, _api?.Logger);
                }
            }
            catch (SourceCodePatchException ex)
            {
                _api?.Logger.Warning($"[VGE] Failed to patch {shaderName}: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Log but don't crash
                _api?.Logger.Error($"[VGE] Unexpected error patching {shaderName}: {ex.Message}");
            }
            finally
            {
                AlreadyPatchedShaders.Add(shaderName);
            }
        }

        /// <summary>
        /// Shaders to patch for G-buffer output injection.
        /// </summary>
        public static HashSet<string> TargetShaders = ["chunkopaque", "chunktopsoil", "standard", "instanced"];
        private static void TryInjectGBuffersIntoShader(SourceCodePatcher source)
        {
            // Only inject into chunk shaders
            string shaderName = source.SourceName;
            if (!TargetShaders.Contains(shaderName))
            {
                return;
            }

            // Use the fluent patcher to inject G-buffer outputs
            source
                .FindVersionDirective().After().Insert(GBufferOutputDeclarations)
                .FindFunction("main").BeforeClose().Insert(GBufferOutputWrites);

            _api?.Logger.Debug($"[VGE] Injected G-buffer outputs into {shaderName}");
        }
    }
}
