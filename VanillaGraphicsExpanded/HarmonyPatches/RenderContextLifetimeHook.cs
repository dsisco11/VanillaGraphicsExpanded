using HarmonyLib;
using OpenTK.Windowing.Desktop;
using VanillaGraphicsExpanded.Rendering.Integration;
namespace VanillaGraphicsExpanded.HarmonyPatches;
/// <summary>Withdraws registered context authority before the native window destroys its context.</summary>
[HarmonyPatch(typeof(NativeWindow), "Dispose", new[] { typeof(bool) })]
internal static class RenderContextLifetimeHook
{
    #region Private
    /// <summary>Retires only this owner, without issuing GL commands or disposing rendering resources.</summary>
    private static void Prefix(NativeWindow __instance) => RenderContextRegistry.Retire(__instance);
    #endregion
}
