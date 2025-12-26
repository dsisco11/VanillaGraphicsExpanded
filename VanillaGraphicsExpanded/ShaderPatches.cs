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
    private static ICoreAPI? _api;

    // Fragment shader code to inject - declares the MRT output at location 4
    // (locations 0-3 are used by outColor, outGlow, outGNormal, outGPosition)
    private const string NormalOutputDeclaration =
        "\n// VGE G-Buffer world-space normal output\nlayout(location = 4) out vec4 vge_outNormal;\n";

    // Code to inject before the final closing brace of main() to write the normal
    // Use 'normal' (world-space) instead of 'gnormal' (view-space)
    private const string NormalOutputWrite =
        "\n    // VGE: Write world-space normal to G-buffer\n    vge_outNormal = vec4(normal * 0.5 + 0.5, 1.0);\n";

    public static void Apply(ICoreAPI api)
    {
        _api = api;
        harmony = new Harmony(HarmonyId);
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

                // Use the fluent shader patcher to inject G-buffer output
                code = new ShaderSourcePatcher(code, shaderName)
                    .AfterVersionDirective().Insert(NormalOutputDeclaration)
                    .BeforeMainClose().Insert(NormalOutputWrite)
                    .Build();

                fragmentShader.Code = code;
                _api?.Logger.Debug($"[VGE] Injected G-buffer output into {shaderName}");
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
