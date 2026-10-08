using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies native graphics settings remain authoritative at the owned postprocessing boundary.</summary>
public sealed class EnginePostprocessInputsTests
{
    #region Public API
    /// <summary>Native settings override stale render flags while the engine-wide postprocessing gate remains effective.</summary>
    [Theory]
    [InlineData(true, true, 1, 2, true)]
    [InlineData(true, false, 0, 0, false)]
    [InlineData(true, true, -1, 1, false)]
    [InlineData(false, true, 2, 2, true)]
    [InlineData(true, false, 5, 0, false)]
    public void CaptureUsesNativeSettingsAndEngineGate(bool enabled, bool bloom, int godRays, int ssao, bool fxaa)
    {
        // Avoid constructing a native window: capture only needs the native postprocessing gate.
        var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        platform.DoPostProcessingEffects = enabled;
        AccessTools.Field(typeof(ClientPlatformWindows), "RenderBloom").SetValue(platform, !bloom);
        AccessTools.Field(typeof(ClientPlatformWindows), "RenderGodRays").SetValue(platform, godRays <= 0);
        AccessTools.Field(typeof(ClientPlatformWindows), "RenderSSAO").SetValue(platform, ssao <= 0);
        AccessTools.Field(typeof(ClientPlatformWindows), "RenderFXAA").SetValue(platform, !fxaa);
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        api.SetupGet(x => x.Settings.Bool["bloom"]).Returns(bloom);
        api.SetupGet(x => x.Settings.Int["godRays"]).Returns(godRays);
        api.SetupGet(x => x.Settings.Int["ssaoQuality"]).Returns(ssao);
        api.SetupGet(x => x.Settings.Bool["fxaa"]).Returns(fxaa);

        var inputs = EnginePostprocessInputs.Capture(platform, api.Object);

        Assert.Equal(enabled && bloom, inputs.Bloom);
        Assert.Equal(enabled && godRays > 0, inputs.LightShafts);
        Assert.Equal(enabled && ssao > 0, inputs.Ssao);
        Assert.Equal(enabled && fxaa, inputs.Fxaa);
        Assert.Equal(ssao, inputs.SsaoQuality);
        Assert.Equal(Math.Clamp(godRays,0,3), inputs.LightShaftQuality);
    }
    #endregion
}
