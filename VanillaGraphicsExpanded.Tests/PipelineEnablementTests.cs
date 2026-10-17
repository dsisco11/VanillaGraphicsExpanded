using System.Reactive.Linq;
using Moq;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Qualifies persistent pipeline conditions against their native and committed configuration sources.</summary>
[Collection("GPU")]
public sealed class PipelineEnablementTests
{
    #region Public API
    /// <summary>Native watcher changes update shared conditions without polling or multiplying subscriptions.</summary>
    [Fact]
    public void NativeConditionsShareSubscriptionsAndReplayDistinctValues()
    {
        bool? previous = PbrShaderLightingMode.GenerationLumOnEnabled;
        PbrShaderLightingMode.GenerationLumOnEnabled = false;
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var ints = new Mock<ISettingsClass<int>>();
        var bools = new Mock<ISettingsClass<bool>>();
        api.SetupGet(value => value.Settings.Int).Returns(ints.Object);
        api.SetupGet(value => value.Settings.Bool).Returns(bools.Object);
        var changes = new Dictionary<string, OnSettingsChanged<int>>();
        ints.Setup(value => value.AddWatcher(It.IsAny<string>(), It.IsAny<OnSettingsChanged<int>>()))
            .Callback<string, OnSettingsChanged<int>>((key, callback) => changes.Add(key, callback));
        try
        {
            using var enablement = new PipelineEnablement(api.Object);
            var states = new List<bool>();
            using var subscription = enablement.DepthHierarchy.Subscribe(states.Add);
            ints.Invocations.Clear(); bools.Invocations.Clear();
            Assert.False(enablement.DepthHierarchy.Value);
            Assert.False(enablement.AmbientOcclusion.Value);
            Assert.False(enablement.LightShaftOcclusion.Value);
            changes["ssaoQuality"](1);
            changes["ssaoQuality"](2);
            Assert.True(enablement.AmbientOcclusion.Value);
            Assert.True(enablement.DepthHierarchy.Value);
            changes["godRays"](2);
            Assert.True(enablement.LightShaftOcclusion.Value);
            Assert.Equal(2, enablement.NativePostprocess.Value.LightShaftQuality);
            PbrShaderLightingMode.GenerationLumOnEnabled = true;
            changes["ssaoQuality"](0);
            Assert.True(enablement.DepthHierarchy.Value);
            PbrShaderLightingMode.GenerationLumOnEnabled = false;
            Assert.False(enablement.DepthHierarchy.Value);
            Assert.Equal(new[] { false, true, false }, states);
            ints.VerifyGet(value => value[It.IsAny<string>()], Times.Never);
            bools.VerifyGet(value => value[It.IsAny<string>()], Times.Never);
            enablement.Dispose();
            ints.Verify(value => value.RemoveWatcher("ssaoQuality", changes["ssaoQuality"]), Times.Once);
            ints.Verify(value => value.RemoveWatcher("godRays", changes["godRays"]), Times.Once);
            changes["ssaoQuality"](1);
            Assert.Equal(new[] { false, true, false }, states);
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled = previous; }
    }

    /// <summary>VGE settings commit together only for the matching client lifetime and use sanitized snapshots.</summary>
    [Fact]
    public void ConfigurationChangesCommitAtomicallyAndIgnoreOtherClients()
    {
        bool? previousGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
        var previous = ConfigModSystem.Config.Postprocessing;
        ConfigModSystem.Config.Postprocessing = new PostprocessSettings
        {
            BloomStrength = 0, LightShaftStrength = 0, LightShaftLimit = 1,
            LocalExposure = new() { Enabled = false }
        };
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        api.SetupGet(value => value.Settings.Bool["bloom"]).Returns(true);
        api.SetupGet(value => value.Settings.Int["godRays"]).Returns(2);
        api.SetupGet(value => value.ModLoader.Systems).Returns(Array.Empty<ModSystem>());
        try
        {
            using var enablement = new PipelineEnablement(api.Object);
            var radiance = new List<bool>();
            using var subscription = enablement.LightShaftRadiance.Subscribe(radiance.Add);
            Assert.False(enablement.Bloom.Value);
            Assert.False(enablement.LocalExposure.Value);
            var config = ConfigModSystem.Config.Postprocessing;
            config.BloomStrength = 1;
            config.LightShaftStrength = 1;
            config.LightShaftLimit = 0;
            config.LocalExposure.Enabled = true;
            Assert.False(enablement.Bloom.Value);
            var other = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
            other.SetupGet(value => value.ModLoader.Systems).Returns(Array.Empty<ModSystem>());
            LiveConfigReload.NotifyAll(other.Object);
            Assert.False(enablement.Bloom.Value);
            LiveConfigReload.NotifyAll(api.Object);
            Assert.True(enablement.Bloom.Value);
            Assert.True(enablement.LocalExposure.Value);
            Assert.Equal(new[] { false }, radiance);
            config.LightShaftLimit = 1;
            LiveConfigReload.NotifyAll(api.Object);
            Assert.True(enablement.LightShaftRadiance.Value);
            Assert.Equal(new[] { false, true }, radiance);
            enablement.Dispose();
            config.LightShaftLimit = 0;
            LiveConfigReload.NotifyAll(api.Object);
            Assert.Equal(new[] { false, true }, radiance);
        }
        finally
        {
            ConfigModSystem.Config.Postprocessing = previous;
            PbrShaderLightingMode.GenerationLumOnEnabled = previousGeneration;
        }
    }
    #endregion
}
