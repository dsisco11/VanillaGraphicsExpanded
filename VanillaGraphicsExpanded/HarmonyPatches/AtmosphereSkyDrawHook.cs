using System;
using System.Reflection;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Suppresses engine sky updates and drawing throughout the VGE sky renderer lifetime.</summary>
[HarmonyPatch]
internal static class AtmosphereSkyDrawHook
{
    #region Public API
    /// <summary>Resolves the installed internal callback without changing its asset lifecycle.</summary>
    internal static MethodBase TargetMethod() => AccessTools.Method(
        typeof(Vintagestory.Client.NoObf.ClientMain).Assembly.GetType("Vintagestory.Client.NoObf.SystemRenderSkyColor"), "OnRenderFrame3D")
        ?? throw new MissingMethodException("Installed sky renderer is unavailable.");

    /// <summary>Resource readiness never reactivates vanilla sky while the replacement is enabled.</summary>
    internal static bool Prefix() => !AtmosphereSkyRenderer.IsEnabled;
    #endregion
}
