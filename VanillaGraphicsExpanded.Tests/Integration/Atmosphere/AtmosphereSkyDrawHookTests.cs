using Moq;
using Vintagestory.API.Client;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Integration.Atmosphere;

/// <summary>Checks complete callback suppression follows the owned renderer lifecycle.</summary>
[Collection("CPU state")]
public sealed class AtmosphereSkyDrawHookTests
{
    #region Public API
    /// <summary>Harmony installs the lifecycle prefix on the actual internal engine callback.</summary>
    [Fact]
    public void HookInstallsOnInstalledEngineMethod()
    {
        // Arrange: resolve the installed engine callback and confirm its containing engine type is available.
        Assert.NotNull(typeof(Vintagestory.Client.NoObf.ClientMain).Assembly.GetType("Vintagestory.Client.NoObf.SystemRenderSkyColor"));
        var method = AtmosphereSkyDrawHook.TargetMethod();
        var harmony = new Harmony("VGE.Tests.AtmosphereSkyDrawHook");
        // Act and Assert: patch the actual callback and verify the prefix appears in Harmony's patch metadata.
        try
        {
            harmony.CreateClassProcessor(typeof(AtmosphereSkyDrawHook)).Patch();
            Assert.Contains(Harmony.GetPatchInfo(method).Prefixes,
                patch => patch.owner == harmony.Id && patch.PatchMethod.DeclaringType == typeof(AtmosphereSkyDrawHook));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    #endregion
}
