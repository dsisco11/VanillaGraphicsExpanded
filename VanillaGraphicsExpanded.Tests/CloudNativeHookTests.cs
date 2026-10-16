using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks that passive observer hooks install against the actual installed native methods.</summary>
[Collection("GPU")]
public sealed class CloudNativeHookTests
{
    #region Public API
    /// <summary>Harmony resolves every observer boundary and none of these input hooks can suppress native rendering.</summary>
    [Fact]
    public void InstalledNativeObserverBoundariesRemainPatchableAndPassive()
    {
        Type[] hooks = [typeof(CloudMapGenerationHook), typeof(CloudMapAllocationHook), typeof(CloudMapUploadHook)];
        var harmony = new Harmony("vge.tests.cloud.native.boundaries");
        try
        {
            foreach (var hook in hooks)
            {
                var prefix = AccessTools.Method(hook, "Prefix");
                if (prefix is not null) Assert.Equal(typeof(void), prefix.ReturnType);
                Assert.NotEmpty(harmony.CreateClassProcessor(hook).Patch());
            }
            Assert.NotEmpty(harmony.CreateClassProcessor(typeof(CloudVolumetricDrawHook)).Patch());
            Assert.NotEmpty(harmony.CreateClassProcessor(typeof(CloudSimpleDrawHook)).Patch());
            Assert.True(CloudSimpleDrawHook.Prefix(
                (Vintagestory.GameContent.CloudRenderer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Vintagestory.GameContent.CloudRenderer)),
                .1f, Vintagestory.API.Client.EnumRenderStage.OIT));
            Assert.True((bool)AccessTools.Method(typeof(CloudVolumetricDrawHook), "Prefix").Invoke(null,
                [System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(FluffyClouds.CloudRendererVolumetric))])!);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    #endregion
}
