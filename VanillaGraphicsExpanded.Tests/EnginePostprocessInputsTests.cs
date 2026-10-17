using System.Runtime.CompilerServices;
using System.Reactive.Disposables;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies native graphics settings remain authoritative at the owned postprocessing boundary.</summary>
[Collection("GPU")]
public sealed class EnginePostprocessInputsTests
{
    #region Public API
    /// <summary>A transient pass gate masks effects without changing cached qualities or persistent eligibility.</summary>
    [Fact]
    public void PassGatePreservesCachedSettingsAndQuality()
    {
        var cached = new EnginePostprocessInputs(true, true, true, true, 2, 3);
        var disabled = cached.ForPass(false);
        Assert.False(disabled.Bloom); Assert.False(disabled.LightShafts);
        Assert.False(disabled.Ssao); Assert.False(disabled.Fxaa);
        Assert.Equal(2, disabled.SsaoQuality); Assert.Equal(3, disabled.LightShaftQuality);
        Assert.True(cached.Ssao); Assert.True(cached.LightShafts);
        Assert.Equal(cached, cached.ForPass(true));
    }

    /// <summary>Native settings override stale render flags while the engine-wide postprocessing gate remains effective.</summary>
    [Theory]
    [InlineData(true, true, 1, 2, true)]
    [InlineData(true, false, 0, 0, false)]
    [InlineData(true, true, -1, 1, false)]
    [InlineData(false, true, 2, 2, true)]
    [InlineData(true, false, 5, 0, false)]
    public void CaptureUsesNativeSettingsAndEngineGate(bool enabled, bool bloom, int godRays, int ssao, bool fxaa)
    {
        bool? previousGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
        using var restoreGeneration = Disposable.Create(() => PbrShaderLightingMode.GenerationLumOnEnabled = previousGeneration);
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

        using var enablement = new PipelineEnablement(api.Object);
        var inputs = enablement.NativePostprocess.Value.ForPass(platform.DoPostProcessingEffects);

        Assert.Equal(enabled && bloom, inputs.Bloom);
        Assert.Equal(enabled && godRays > 0, inputs.LightShafts);
        Assert.Equal(enabled && ssao > 0, inputs.Ssao);
        Assert.Equal(enabled && fxaa, inputs.Fxaa);
        Assert.Equal(ssao, inputs.SsaoQuality);
        Assert.Equal(Math.Clamp(godRays,0,3), inputs.LightShaftQuality);
    }
    /// <summary>Persisted bloom controls retain bypass semantics and bound finite graph work.</summary>
    [Fact]
    public void BloomSnapshotPreservesBypassAndBoundsGraph()
    {
        var settings=new PostprocessSettings {BloomThreshold=0,BloomKnee=0,BloomLevels=99};
        var snapshot=settings.Snapshot();
        Assert.Equal(0,snapshot.BloomThreshold);Assert.Equal(0,snapshot.BloomKnee);Assert.Equal(6,snapshot.BloomLevels);
        settings.BloomThreshold=float.NaN;settings.BloomStrength=float.PositiveInfinity;settings.BloomLevels=-1;
        snapshot=settings.Snapshot();
        Assert.Equal(1,snapshot.BloomThreshold);Assert.Equal(.08f,snapshot.BloomStrength);Assert.Equal(3,snapshot.BloomLevels);
    }
    #endregion
}
