using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;

namespace VanillaGraphicsExpanded.Tests.Integration.Clouds;

/// <summary>Checks that passive observer hooks install against the actual installed native methods.</summary>
[Collection("CPU state")]
public sealed class CloudNativeHookTests
{
    #region Public API
    /// <summary>Harmony resolves every observer boundary and none of these input hooks can suppress native rendering.</summary>
    [Fact]
    public void InstalledNativeObserverBoundariesRemainPatchableAndPassive()
    {
        // Arrange: identify installed observer and draw hooks and prepare a scoped Harmony owner.
        Type[] hooks = [typeof(CloudMapGenerationHook), typeof(CloudMapAllocationHook), typeof(CloudMapUploadHook)];
        var harmony = new Harmony("vge.tests.cloud.native.boundaries");
        try
        {
            // Act/Assert: resolve each hook against the installed native method and verify it can be patched.
            foreach (var hook in hooks)
            {
                var prefix = AccessTools.Method(hook, "Prefix");
                if (prefix is not null) Assert.Equal(typeof(void), prefix.ReturnType);
                Assert.NotEmpty(harmony.CreateClassProcessor(hook).Patch());
            }
            Assert.NotEmpty(harmony.CreateClassProcessor(typeof(CloudVolumetricDrawHook)).Patch());
            Assert.NotEmpty(harmony.CreateClassProcessor(typeof(CloudSimpleDrawHook)).Patch());
            // Act/Assert: invoke the draw prefixes without an admitted VGE owner; native rendering remains enabled.
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
