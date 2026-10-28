using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies installation of the retained native-window ownership integration.</summary>
[Collection("GPU")]
public sealed class EngineStateSwitchingTests
{
    #region Public API
    /// <summary>The native-window disposal boundary accepts its actual installed Harmony patch signature.</summary>
    [Fact]
    public void ContextDisposalHookCanBeInstalled()
    {
        var harmony = new Harmony("VGE.Tests.ContextDisposalHook");
        try
        {
            var patched = harmony.CreateClassProcessor(typeof(RenderContextLifetimeHook)).Patch();
            var target = AccessTools.Method(typeof(OpenTK.Windowing.Desktop.NativeWindow), "Dispose", [typeof(bool)]);
            Assert.Single(patched);
            Assert.Contains(Harmony.GetPatchInfo(target).Prefixes, patch => patch.owner == harmony.Id);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    #endregion
}
