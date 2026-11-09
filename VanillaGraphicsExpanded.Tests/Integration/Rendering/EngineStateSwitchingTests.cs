using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies installation of the retained native-window ownership integration.</summary>
[Collection("CPU state")]
public sealed class EngineStateSwitchingTests
{
    #region Public API
    /// <summary>The native-window disposal boundary accepts its actual installed Harmony patch signature.</summary>
    [Fact]
    public void ContextDisposalHookCanBeInstalled()
    {
        // Arrange: create a uniquely owned Harmony patch set for the native disposal boundary.
        var harmony = new Harmony("VGE.Tests.ContextDisposalHook");
        try
        {
            // Act: install the production context-lifetime hook.
            var patched = harmony.CreateClassProcessor(typeof(RenderContextLifetimeHook)).Patch();
            var target = AccessTools.Method(typeof(OpenTK.Windowing.Desktop.NativeWindow), "Dispose", [typeof(bool)]);
            // Assert: the expected prefix is attached to the real native method signature.
            Assert.Single(patched);
            Assert.Contains(Harmony.GetPatchInfo(target).Prefixes, patch => patch.owner == harmony.Id);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    #endregion
}
