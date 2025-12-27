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
/// Harmony patches for shader-related injection points.
/// This class only handles WHERE patches are applied, not WHAT is patched.
/// </summary>
public static class ShaderPatches
{
    private const string HarmonyId = "vanillagraphicsexpanded.shaderpatches";
    
    private static HarmonyInstance? harmony;
    private static ICoreAPI? _api;

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
}
