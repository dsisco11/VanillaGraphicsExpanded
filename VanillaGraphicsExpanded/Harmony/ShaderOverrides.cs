using HarmonyLib;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Harmony;

/// <summary>
/// Harmony patches for overriding shader program properties.
/// </summary>
[HarmonyPatch]
public static class ShaderOverrides
{
    // NOTE: Currently unused, now we just patch the shader includes to universally kill normal shading.
    /// <summary>
    /// Prefix patch that intercepts the NormalShaded setter.
    /// We override and always set it to 0 so that shaders always have normal shading disabled, since our shaders are better.
    /// </summary>
    /// <param name="__instance">The ChunkOpaque shader instance.</param>
    /// <param name="value">The value being set.</param>
    /// <returns>False to skip original implementation, true to run it.</returns>
    //[HarmonyPatch(nameof(Vintagestory.Client.NoObf.ShaderProgramChunkopaque), "set_NormalShaded")]
    // [HarmonyPatch(nameof(Vintagestory.Client.NoObf.ShaderProgramStandard), "set_NormalShaded")]
    // [HarmonyPatch(nameof(Vintagestory.Client.NoObf.ShaderProgramHelditem), "set_NormalShaded")]
    // [HarmonyPrefix]
    // public static bool NormalShadedSetterPrefix(IShaderProgram __instance, int value)
    // {
    //     __instance.Uniform("normalShaded", 0);
    //     return false;
    // }
}
