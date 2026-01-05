using System;
using System.Linq;

using TinyTokenizer.Ast;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Handles WHAT modifications are applied to vanilla shader assets.
/// Responsible for defining and applying shader code patches using TinyAst.
/// </summary>
internal static class VanillaShaderPatches
{
    #region G-Buffer Injection Code

    // G-Buffer output declarations (locations 4-5, after VS's 0-3)
    // Location 4: World-space normals (RGBA16F)
    // Location 5: Material properties (RGBA16F) - Reflectivity, Roughness, Metallic, Emissive
    // Note: VS's ColorAttachment0 (outColor) serves as albedo
    private const string GBufferInputDeclarations = @"
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
    float vge_roughness = 0.5;  // Default roughness (could be extracted from texture)
    float vge_metallic = getMatMetallicFromRenderFlags(renderFlags);
    float vge_emissive = glowLevel;
    float vge_reflectivity = getMatMetallicFromRenderFlags(renderFlags); // Using same function for reflectivity for simplicity
    vge_outMaterial = vec4(vge_roughness, vge_metallic, vge_emissive, vge_reflectivity);
";
    #endregion

    /// <summary>
    /// Attempts to apply pre-processing patches BEFORE imports are inlined.
    /// Use this for modifications that need to happen on the raw shader source.
    /// </summary>
    /// <param name="log">Logger for warnings/errors.</param>
    /// <param name="tree">The SyntaxTree instance (imports NOT yet processed).</param>
    /// <param name="sourceName">The name of the shader source.</param>
    /// <returns>True if pre-processing patches were applied, false otherwise.</returns>
    internal static bool TryApplyPreProcessing(ILogger? log, SyntaxTree tree, string sourceName)
    {
        try
        {
            switch (sourceName)
            {
                // Main shader files - inject vsFunctions import
                case "chunktransparent.fsh":
                case "chunkopaque.fsh":
                case "chunktopsoil.fsh":
                case "instanced.fsh":
                //case "standard.fsh":
                    {
                        // Find the location of the last glDirective at the top (after #version)
                        //var versionDirectiveQuery = Query.Syntax<GlDirectiveNode>().Named("version");
                        // Find main function and insert @import before it
                        var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");

                        //var versionNode = tree.Select(versionDirectiveQuery).First() as GlDirectiveNode;

                        tree.CreateEditor()
                            .Insert(mainQuery.Before(), "@import \"vsFunctions.glsl\"\n")
                            .Commit();

                        log?.Audit($"[VGE] Applied pre-processing to shader: {sourceName}");
                        return true;
                    }

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE] Failed to pre-process shader '{sourceName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Attempts to apply patches to the given SyntaxTree based on the shader name.
    /// </summary>
    /// <param name="log">Logger for warnings/errors.</param>
    /// <param name="tree">The SyntaxTree instance (already has imports processed).</param>
    /// <param name="sourceName">The name of the shader source.</param>
    /// <returns>True if patches were applied, false if no patches needed for this shader.</returns>
    internal static bool TryApplyPatches(ILogger? log, SyntaxTree tree, string sourceName)
    {
        try
        {
            switch (sourceName)
            {
                // Shader includes
                case "fogandlight.fsh":
                    {
                        PatchFogAndLight(tree);
                        log?.Audit($"[VGE] Applied patches to shader: {sourceName}");
                        return true;
                    }
                // case "normalshading.fsh": // Note: Disabled since we don't really care to change the lighting for gui items or first-person view items.
                //     PatchNormalshading(tree);
                //     return true;
                // Main shader files - inject G-buffer outputs
                case "chunktransparent.fsh":
                case "chunkopaque.fsh":
                case "chunktopsoil.fsh":
                case "instanced.fsh":
                //case "standard.fsh":
                    {
                        InjectGBufferInputs(tree);
                        InjectGBufferOutputs(tree);
                        log?.Audit($"[VGE] Applied patches to shader: {sourceName}");
                        return true;
                    }
                case "sky.fsh":
                    {
                        InjectGBufferInputs(tree);
                        InjectSkyGBufferOutputs(tree);
                        log?.Audit($"[VGE] Applied patches to shader: {sourceName}");
                        return true;
                    }
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE] Failed to patch shader '{sourceName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Injects G-buffer output declarations after #version directive.
    /// </summary>
    private static void InjectGBufferInputs(SyntaxTree tree)
    {
        // Find the #version directive and insert after it
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");

        tree.CreateEditor()
            .Insert(versionQuery.After(), GBufferInputDeclarations)
            .Commit();
    }

    /// <summary>
    /// Injects G-buffer output writes at the end of main() function body.
    /// This ensures normal, glowLevel, and renderFlags have been computed.
    /// </summary>
    private static void InjectGBufferOutputs(SyntaxTree tree)
    {
        // Find main function and insert at inner end of body (before closing brace)
        var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");

        tree.CreateEditor()
            .Insert(mainQuery.InnerStart("body"), GBufferOutputWrites)
            .Commit();
    }

    private static void InjectSkyGBufferOutputs(SyntaxTree tree)
    {
        // Sky shader only needs to write default values to G-buffer outputs
        const string skyGBufferWrites = @"
    // VGE: Write default G-buffer outputs for sky
    vge_outNormal = vec4(0.0); // Upward normal
    vge_outMaterial = vec4(1.0, 0.0, outGlow.g, 0.0); // Default material properties
";

        // Find main function and insert at inner end of body (before closing brace)
        var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");

        tree.CreateEditor()
            .Insert(mainQuery.InnerEnd("body"), skyGBufferWrites)
            .Commit();
    }

    /// <summary>
    /// Patches the fogandlight.fsh shader to disable fog, shadow, and normal shading effects.
    /// </summary>
    private static void PatchFogAndLight(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();

        // intercept 'applyFog' function and just return unadjusted color
        InsertAtFunctionTop(editor, "applyFog", "\nreturn rgbaPixel;");

        // intercept 'getBrightnessFromShadowMap' function and return full brightness
        InsertAtFunctionTop(editor, "getBrightnessFromShadowMap", "\nreturn 1.0;");

        // intercept 'getBrightnessFromNormal' function and return full brightness
        InsertAtFunctionTop(editor, "getBrightnessFromNormal", "\nreturn 1.0;");

        // intercept 'applyFogAndShadow' function and just return unadjusted color
        InsertAtFunctionTop(editor, "applyFogAndShadow", "\nreturn rgbaPixel;");

        // intercept 'applyFogAndShadowWithNormal' function and just return unadjusted color
        InsertAtFunctionTop(editor, "applyFogAndShadowWithNormal", "\nreturn rgbaPixel;");

        // intercept 'applyFogAndShadowFromBrightness' function and just return unadjusted color
        InsertAtFunctionTop(editor, "applyFogAndShadowFromBrightness", "\nreturn rgbaPixel;");

        editor.Commit();
    }

    /// <summary>
    /// Helper method to insert code at the top of a function body using the editor.
    /// </summary>
    private static void InsertAtFunctionTop(SyntaxEditor editor, string functionName, string code)
    {
        var funcQuery = Query.Syntax<GlFunctionNode>().Named(functionName);
        editor.Insert(funcQuery.InnerStart("body"), code);
    }

    /// <summary>
    /// Patches the normalshading.fsh shader to return full brightness.
    /// </summary>
    private static void PatchNormalshading(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();

        // intercept 'getBrightnessFromNormal' function and return full brightness
        InsertAtFunctionTop(editor, "getBrightnessFromNormal", "\nreturn 1.0;");

        editor.Commit();
    }
}