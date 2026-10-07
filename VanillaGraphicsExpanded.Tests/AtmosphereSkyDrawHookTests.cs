using Moq;
using Vintagestory.API.Client;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks complete callback suppression follows the owned renderer lifecycle.</summary>
[Collection("GPU")]
public sealed class AtmosphereSkyDrawHookTests
{
    #region Public API
    /// <summary>Enabled sky ownership suppresses vanilla even before resources or lighting are ready.</summary>
    [Fact]
    public void PrefixFollowsRendererLifecycleWithoutResourceAdmission()
    {
        Assert.True(AtmosphereSkyDrawHook.Prefix());
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        using (var sky = new AtmosphereSkyRenderer(api.Object, null!))
        {
            Assert.True(AtmosphereSkyRenderer.IsEnabled);
            Assert.False(AtmosphereSkyDrawHook.Prefix());
        }
        Assert.False(AtmosphereSkyRenderer.IsEnabled);
        Assert.True(AtmosphereSkyDrawHook.Prefix());
    }

    /// <summary>Harmony installs the lifecycle prefix on the actual internal engine callback.</summary>
    [Fact]
    public void HookInstallsOnInstalledEngineMethod()
    {
        Assert.NotNull(typeof(Vintagestory.Client.NoObf.ClientMain).Assembly.GetType("Vintagestory.Client.NoObf.SystemRenderSkyColor"));
        var method = AtmosphereSkyDrawHook.TargetMethod();
        var harmony = new Harmony("VGE.Tests.AtmosphereSkyDrawHook");
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

