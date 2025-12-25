using HarmonyLib;

using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Harmony patches to inject G-buffer normal output into chunk shaders.
/// </summary>
public static class ShaderPatches
{
    private const string HarmonyId = "vanillagraphicsexpanded.shaderpatches";
    private static Harmony? harmony;

    // Fragment shader code to inject - declares the MRT output at location 4
    // (locations 0-3 are used by outColor, outGlow, outGNormal, outGPosition)
    private const string NormalOutputDeclaration = @"
// VGE G-Buffer world-space normal output
layout(location = 4) out vec4 vge_outNormal;
";

    // Code to inject before the final closing brace of main() to write the normal
    // Use 'normal' (world-space) instead of 'gnormal' (view-space)
    private const string NormalOutputWrite = @"
    // VGE: Write world-space normal to G-buffer
    vge_outNormal = vec4(normal * 0.5 + 0.5, 1.0);
";

    public static void Apply(ICoreAPI api)
    {
        harmony = new Harmony(HarmonyId);
        harmony.PatchAll();
        api.Logger.Notification("[VGE] Harmony shader patches applied");
    }

    public static void Unpatch(ICoreAPI? api)
    {
        harmony?.UnpatchAll(HarmonyId);
        api?.Logger.Notification("[VGE] Harmony shader patches removed");
    }

    /// <summary>
    /// Patches ShaderRegistry.RegisterShaderProgram to intercept chunk shader compilation.
    /// </summary>
    [HarmonyPatch]
    public static class RegisterShaderProgramPatch
    {
        public static HashSet<string> PatchedShaders = ["chunkopaque", "chunktopsoil", "standard", "instanced"];
        [HarmonyPatch(typeof(Vintagestory.Client.NoObf.ShaderRegistry), "LoadShaderProgram")]
        [HarmonyPostfix]
        static void LoadShaderProgram(Vintagestory.Client.NoObf.ShaderProgram program, bool useSSBOs)
        {
            // Only inject into chunk shaders
            string shaderName = program.PassName;
            if (!PatchedShaders.Contains(shaderName))
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
                if (code.Contains("vge_outNormal")) return;

                // Inject the output declaration after #version and any existing layout declarations
                // Find position after #version line
                int versionEnd = code.IndexOf('\n', code.IndexOf("#version"));
                if (versionEnd < 0) versionEnd = 0;

                // Insert declaration
                code = code.Insert(versionEnd + 1, NormalOutputDeclaration);

                // Find the closing brace of main() and insert the write before it
                int mainEnd = TextPatcher.FindMainFunctionClosingBrace(code);
                if (mainEnd > 0)
                {
                    code = code.Insert(mainEnd, NormalOutputWrite);
                }

                fragmentShader.Code = code;
                System.Diagnostics.Debug.WriteLine($"[VGE] Injected G-buffer output into {shaderName}");
            }
            catch (Exception ex)
            {
                // Log but don't crash
                System.Diagnostics.Debug.WriteLine($"[VGE] Failed to inject into {shaderName}: {ex.Message}");
            }
        }
    }
}
