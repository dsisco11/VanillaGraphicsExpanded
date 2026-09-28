using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Labels screen rendering outside world stages, including menu and default-framebuffer UI work.</summary>
[HarmonyPatch]
internal static class EngineScreenScopeHook
{
    private static readonly Dictionary<MethodBase, string> Names = new();

    #region Patch selection
    /// <summary>Installs capture instrumentation only in debug builds.</summary>
    [HarmonyPrepare]
    private static bool Prepare()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    /// <summary>Discovers concrete engine screen implementations of the five screen render boundaries.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        var assembly = typeof(ClientMain).Assembly;
        var screen = typeof(GuiScreen);
        var boundaries = new HashSet<string>
        {
            nameof(GuiScreen.RenderToPrimary), nameof(GuiScreen.RenderAfterPostProcessing), nameof(GuiScreen.RenderAfterFinalComposition),
            nameof(GuiScreen.RenderAfterBlit), nameof(GuiScreen.RenderToDefaultFramebuffer)
        };
        // Discover overrides rather than naming individual loading, menu and game screens.
        foreach (var type in assembly.GetTypes())
        {
            if (!screen.IsAssignableFrom(type)) continue;
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.IsAbstract || !boundaries.Contains(method.Name) || method.GetParameters() is not [{ ParameterType: var parameter }] || parameter != typeof(float)) continue;
                // Keep the screen identity and boundary, without redundant engine/category prefixes.
                string screenName = type.Name.StartsWith(nameof(GuiScreen), StringComparison.Ordinal)
                    ? type.Name[nameof(GuiScreen).Length..] : type.Name;
                if (screenName.Length == 0) screenName = "Screen";
                string boundary = method.Name.StartsWith("Render", StringComparison.Ordinal) ? method.Name[6..] : method.Name;
                Names[method] = $"{screenName}.{boundary}";
                yield return method;
            }
        }
    }
    #endregion

    #region Scope lifecycle
    /// <summary>Opens the cached engine screen boundary name.</summary>
    [HarmonyPrefix]
    internal static void Prefix(MethodBase __originalMethod, out GlDebug.GroupScope __state) =>
        __state = GlDebug.Group(Names[__originalMethod]);

    /// <summary>Closes the screen scope without changing exception propagation.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(GlDebug.GroupScope __state) => __state.Dispose();
    #endregion
}
