using HarmonyLib;

using System;

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

    // Fragment shader code to inject - declares the MRT output
    private const string NormalOutputDeclaration = @"
// VGE G-Buffer normal output
layout(location = 1) out vec4 vge_outNormal;
";

    // Code to inject before the final closing brace of main() to write the normal
    // This writes the gnormal variable that chunk shaders already compute
    private const string NormalOutputWrite = @"
    // VGE: Write normal to G-buffer
    vge_outNormal = vec4(gnormal * 0.5 + 0.5, 1.0);
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
    public static class RegisterShaderProgramPatch
    {
        //[HarmonyPatch(typeof(Vintagestory.Client.NoObf.ShaderRegistry), nameof(Vintagestory.Client.NoObf.ShaderRegistry.RegisterShaderProgram), [typeof(EnumShaderProgram), typeof(Vintagestory.Client.NoObf.ShaderProgram)])]
        //static void Prefix(EnumShaderProgram defaultProgram, Vintagestory.Client.NoObf.ShaderProgram program)
        [HarmonyPatch(typeof(Vintagestory.Client.NoObf.ShaderRegistry), "LoadShaderProgram")]
        [HarmonyPostfix]
        static void LoadShaderProgram(Vintagestory.Client.NoObf.ShaderProgram program, bool useSSBOs)
        {
            // Only inject into chunk shaders
            string shaderName = program.PassName;
            if (shaderName != "chunkopaque" &&
                shaderName != "chunkliquid" &&
                shaderName != "chunktopsoil")
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

                // Find the last closing brace of main() and insert the write before it
                // Look for pattern like "outColor = ..." or similar final assignment, then the closing }
                int mainEnd = code.LastIndexOf('}');
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
