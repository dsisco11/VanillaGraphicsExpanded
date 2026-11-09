using System.Runtime.CompilerServices;
using System.Reactive.Disposables;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Checks native engine flags and client settings compose into owned postprocessing inputs.</summary>
[Collection("CPU state")]
public sealed class EnginePostprocessInputPolicyTests
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
        // Arrange: control native flags and settings without constructing the native window.
        bool? previousGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
        using var restoreGeneration = Disposable.Create(() => PbrShaderLightingMode.GenerationLumOnEnabled = previousGeneration);
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

        using var enablement = new PipelineEnablement(api.Object);
        // Act: capture settings through the production postprocessing input boundary.
        var inputs = enablement.NativePostprocess.Value.ForPass(platform.DoPostProcessingEffects);

        // Assert: settings override stale flags, while the engine-wide gate and quality bounds apply.
        Assert.Equal(enabled && bloom, inputs.Bloom);
        Assert.Equal(enabled && godRays > 0, inputs.LightShafts);
        Assert.Equal(enabled && ssao > 0, inputs.Ssao);
        Assert.Equal(enabled && fxaa, inputs.Fxaa);
        Assert.Equal(ssao, inputs.SsaoQuality);
        Assert.Equal(Math.Clamp(godRays, 0, 3), inputs.LightShaftQuality);
    }
    #endregion
}
