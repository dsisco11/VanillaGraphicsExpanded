using Moq;
using Vintagestory.API.Client;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Checks complete callback suppression follows the owned renderer lifecycle.</summary>
[Collection("CPU state")]
public sealed class AtmosphereSkyDrawHookTests
{
    #region Public API
    /// <summary>Enabled sky ownership suppresses vanilla even before resources or lighting are ready.</summary>
    [Fact]
    public void PrefixFollowsRendererLifecycleWithoutResourceAdmission()
    {
        // Arrange: begin without a SkyRenderer owner, then use a default API mock to exercise renderer enablement.
        // Act and Assert: perform the operation and check its observable result.
        Assert.True(AtmosphereSkyDrawHook.Prefix());
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        using (var sky = new SkyRenderer(api.Object, null!))
        {
            Assert.True(SkyRenderer.IsEnabled);
            Assert.False(AtmosphereSkyDrawHook.Prefix());
        }
        Assert.False(SkyRenderer.IsEnabled);
        Assert.True(AtmosphereSkyDrawHook.Prefix());
    }
    #endregion
}
