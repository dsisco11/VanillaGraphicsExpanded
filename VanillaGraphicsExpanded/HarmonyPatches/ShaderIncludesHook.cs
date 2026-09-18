using HarmonyLib;

using System;
using System.Collections.Generic;
using System.Text;

using TinyTokenizer.Ast;

using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Shaders;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>
/// Harmony patch that intercepts shader loading to apply import inlining and shader modifications.
/// This class only defines WHERE the patch is applied, delegating WHAT to do to other systems.
/// </summary>
[HarmonyPatch]
public static class ShaderIncludesHook
{
    private static ILogger? _logger;
    private static IAssetManager? _assetManager;

    // VGE shader programs inline imports themselves (VgeShaderProgram + ShaderSourceCode).
    // Skip VGE-owned shader programs here to avoid double-processing and wasted tokenization.

    /// <summary>
    /// Initializes the hook with dependencies.
    /// Called from ShaderPatches.Apply().
    /// </summary>
    public static void Initialize(ILogger? logger, IAssetManager? assetManager)
    {
        _logger = logger;
        _assetManager = assetManager;
    }

    [HarmonyPatch(typeof(Vintagestory.Client.NoObf.ShaderRegistry), "LoadShaderProgram")]
    [HarmonyPostfix]
    public static void LoadShaderProgram_Hook(Vintagestory.Client.NoObf.ShaderProgram program, bool useSSBOs)
    {
        if (program is null)
        {
            return;
        }
        if (_logger is null)
        {
            return;
        }

        // VGE-owned shaders are compiled from already-inlined sources via ShaderSourceCode.
        // Let the engine load/patch vanilla shaders only.
        if (string.Equals(program.AssetDomain, Constants.ModId, System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        //_logger.Audit($"[VGE][Shaders] Processing shader program '{program.PassName}'");
        ProcessShaderProgram(program);
    }

    /// <summary>
    /// Prefix patch for ShaderRegistry.loadRegisteredShaderPrograms.
    /// Processes shader assets in-place before the original method compiles them.
    /// </summary>
    //[HarmonyPatch(typeof(Vintagestory.Client.NoObf.ShaderRegistry), "loadRegisteredShaderPrograms")]
    //[HarmonyPrefix]
    //public static void loadRegisteredShaderPrograms_Hook()
    //{
    //    if (_assetManager is null)
    //    {
    //        _logger?.Warning("[VGE][Shaders] AssetManager not available");
    //        return;
    //    }

    //    // Process shader includes first (stored under shaders/includes/)
    //    List<IAsset> shaderIncludes = _assetManager.GetManyInCategory(
    //        AssetCategory.shaders.Code,
    //        pathBegins: "includes",
    //        domain: null,
    //        loadAsset: true);
        
    //    //_logger?.Audit($"[VGE][Shaders] Processing {shaderIncludes.Count} shader includes");
    //    int patchedCount = ProcessShaderAssets(shaderIncludes);
    //    if (patchedCount > 0)
    //    {
    //        _logger?.Audit($"[VGE][Shaders] Patched {patchedCount} shader include(s)");
    //    }

    //    // Process main shader source files
    //    List<IAsset> shaderSources = _assetManager.GetManyInCategory(
    //        AssetCategory.shaders.Code,
    //        pathBegins: "",
    //        domain: null,
    //        loadAsset: true);
        
    //    //_logger?.Audit($"[VGE][Shaders] Processing {shaderSources.Count} shader source files");
    //    patchedCount = ProcessShaderAssets(shaderSources);
    //    if (patchedCount > 0)
    //    {
    //        _logger?.Notification($"[VGE][Shaders] Patched {patchedCount} shader source file(s)");
    //    }
    //}

    private static void ProcessShaderProgram(in IShaderProgram shaderProgram)
    {
        var candidates = new List<(IShader Shader, string Source)>();

        if (!TryProcessShader(shaderProgram.VertexShader, $"{shaderProgram.PassName}.vsh", preProcess: true, inlineImports: true, postProcess: true, out string? vertexSource))
        {
            return;
        }
        if (vertexSource is not null)
        {
            candidates.Add((shaderProgram.VertexShader, vertexSource));
        }

        if (!TryProcessShader(shaderProgram.FragmentShader, $"{shaderProgram.PassName}.fsh", preProcess: true, inlineImports: true, postProcess: true, out string? fragmentSource))
        {
            return;
        }
        if (fragmentSource is not null)
        {
            candidates.Add((shaderProgram.FragmentShader, fragmentSource));
        }

        if (shaderProgram.GeometryShader is not null)
        {
            if (!TryProcessShader(shaderProgram.GeometryShader, $"{shaderProgram.PassName}.gsh", preProcess: true, inlineImports: true, postProcess: true, out string? geometrySource))
            {
                return;
            }
            if (geometrySource is not null)
            {
                candidates.Add((shaderProgram.GeometryShader, geometrySource));
            }
        }

        foreach (var candidate in candidates)
        {
            ShaderType shaderType = candidate.Shader switch
            {
                var s when s == shaderProgram.VertexShader => ShaderType.VertexShader,
                var s when s == shaderProgram.FragmentShader => ShaderType.FragmentShader,
                var s when s == shaderProgram.GeometryShader => ShaderType.GeometryShader,
                _ => throw new InvalidOperationException("Unknown shader stage")
            };
            string stageExtension = shaderType switch
            {
                ShaderType.VertexShader => "vsh",
                ShaderType.FragmentShader => "fsh",
                ShaderType.GeometryShader => "gsh",
                _ => throw new InvalidOperationException("Unknown shader stage")
            };

            if (!GlslCompileDiagnostics.TryCompileStage(shaderType, candidate.Source, out string infoLog))
            {
                _logger?.Error($"[VGE] Skipping shader patches for '{shaderProgram.PassName}': {stageExtension} candidate failed validation. {infoLog}");
                return;
            }
        }

        foreach (var candidate in candidates)
        {
            candidate.Shader.Code = candidate.Source;
        }
    }

    /// <summary>
    /// Processes a single shader through the full pipeline:
    /// 1. Pre-processing (before imports)
    /// 2. Import inlining
    /// 3. Post-processing (after imports)
    /// </summary>
    private static bool TryProcessShader(
        in IShader shader,
        string shaderName,
        bool preProcess,
        bool inlineImports,
        bool postProcess,
        out string? candidateSource)
    {
        candidateSource = null;

        // Create SyntaxTree without processing imports yet
        var tree = ShaderImportsSystem.Instance.CreateSyntaxTree(shader.Code, shaderName);
        if (tree is null)
        {
            return true;
        }

        bool hasChanges = false;

        // Stage 1: Pre-processing (before imports are inlined)
        if (preProcess)
        {
            hasChanges |= VanillaShaderPatches.TryApplyPreProcessing(_logger, tree, shaderName);
        }
        // Stage 2: Inline imports
        if (inlineImports)
        {
            // Import inlining is VGE-owned and safe for VGE shaders; vanilla patch injection remains separate.
            var preprocess = GlslPreprocessor.InlineImports(tree, shaderName, _logger);
            tree = preprocess.OutputTree;
            hasChanges |= preprocess.HadImports;
        }
        // Stage 3: Post-processing (after imports are inlined)
        if (postProcess)
        {
            hasChanges |= VanillaShaderPatches.TryApplyPatches(_logger, tree, shaderName);
        }

        if (hasChanges)
        {
            // Build, strip non-ASCII (GLSL compliance), and write back to shader
            candidateSource = SourceCodeImportsProcessor.StripNonAscii(tree.ToText());
        }

        return true;
    }

    /// <summary>
    /// Processes a list of shader assets through the full pipeline:
    /// 1. Pre-processing (before imports)
    /// 2. Import inlining
    /// 3. Post-processing (after imports)
    /// Single tokenization pass per asset.
    /// </summary>
    private static int ProcessShaderAssets(List<IAsset> assets)
    {
        int patchedCount = 0;
        
        foreach (IAsset asset in assets)
        {
            // Create SyntaxTree without processing imports yet
            var tree = ShaderImportsSystem.Instance.CreateSyntaxTree(asset);
            if (tree is null)
            {
                continue;
            }

            bool hasChanges = false;

            // Stage 1: Pre-processing (before imports are inlined)
            hasChanges |= VanillaShaderPatches.TryApplyPreProcessing(_logger, tree, asset.Name);

            // Stage 2: Inline imports
            if (ShaderImportsSystem.Instance.TryPreprocessImports(tree, asset.Name, out var processedTree, _logger))
            {
                tree = processedTree;
                hasChanges = true;
            }

            // Stage 3: Post-processing (after imports are inlined)
            hasChanges |= VanillaShaderPatches.TryApplyPatches(_logger, tree, asset.Name);

            // Build, strip non-ASCII (GLSL compliance), and write back to asset
            if (hasChanges)
            {
                asset.Data = Encoding.UTF8.GetBytes(SourceCodeImportsProcessor.StripNonAscii(tree.ToText()));
                asset.IsPatched = true;
                patchedCount++;
            }
        }

        return patchedCount;
    }
}
