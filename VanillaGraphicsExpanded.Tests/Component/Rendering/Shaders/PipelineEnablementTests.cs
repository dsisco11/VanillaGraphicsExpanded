using System.Reactive.Linq;
using Moq;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering.Shaders;

/// <summary>Qualifies persistent pipeline conditions against their native and committed configuration sources.</summary>
[Collection("CPU state")]
public sealed class PipelineEnablementTests
{
    #region Public API
    /// <summary>Native watcher changes update shared conditions without polling or multiplying subscriptions.</summary>
    [Fact]
    public void NativeConditionsShareSubscriptionsAndReplayDistinctValues()
    {
        // Arrange
        bool? previous = PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled = false;
            var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
            var ints = new Mock<ISettingsClass<int>>();
            var bools = new Mock<ISettingsClass<bool>>();
            api.SetupGet(value => value.Settings.Int).Returns(ints.Object);
            api.SetupGet(value => value.Settings.Bool).Returns(bools.Object);
            var changes = new Dictionary<string, OnSettingsChanged<int>>();
            ints.Setup(value => value.AddWatcher(It.IsAny<string>(), It.IsAny<OnSettingsChanged<int>>()))
                .Callback<string, OnSettingsChanged<int>>((key, callback) => changes.Add(key, callback));

            // Act
            using var enablement = new PipelineEnablement(api.Object);
            var states = new List<bool>();
            using var subscription = enablement.DepthHierarchy.Subscribe(states.Add);
            ints.Invocations.Clear(); bools.Invocations.Clear();
            // Assert construction observes the controlled disabled settings.
            Assert.False(enablement.DepthHierarchy.Value);
            Assert.False(enablement.AmbientOcclusion.Value);
            Assert.False(enablement.LightShaftOcclusion.Value);

            // Act: deliver distinct native setting changes through the registered callbacks.
            changes["ssaoQuality"](1);
            changes["ssaoQuality"](2);
            // Assert both exposed conditions follow the shared quality watcher.
            Assert.True(enablement.AmbientOcclusion.Value);
            Assert.True(enablement.DepthHierarchy.Value);
            // Act and assert the separate light-shaft watcher publishes the requested quality.
            changes["godRays"](2);
            Assert.True(enablement.LightShaftOcclusion.Value);
            Assert.Equal(2, enablement.NativePostprocess.Value.LightShaftQuality);

            // Act: toggle the global lighting mode and deliver another native change.
            PbrShaderLightingMode.GenerationLumOnEnabled = true;
            changes["ssaoQuality"](0);
            // Assert the setting remains enabled while generation requires it.
            Assert.True(enablement.DepthHierarchy.Value);
            // Act and assert disabling the generation mode reevaluates the same condition.
            PbrShaderLightingMode.GenerationLumOnEnabled = false;
            Assert.False(enablement.DepthHierarchy.Value);
            Assert.Equal(new[] { false, true, false }, states);
            ints.VerifyGet(value => value[It.IsAny<string>()], Times.Never);
            bools.VerifyGet(value => value[It.IsAny<string>()], Times.Never);

            // Act: dispose the owner, then deliver one late callback.
            enablement.Dispose();
            // Assert disposal unregisters watchers and prevents further publication.
            ints.Verify(value => value.RemoveWatcher("ssaoQuality", changes["ssaoQuality"]), Times.Once);
            ints.Verify(value => value.RemoveWatcher("godRays", changes["godRays"]), Times.Once);
            // Act and assert the captured callback cannot publish after its owner is disposed.
            changes["ssaoQuality"](1);
            Assert.Equal(new[] { false, true, false }, states);
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled = previous; }
    }

    /// <summary>VGE settings commit together only for the matching client lifetime and use sanitized snapshots.</summary>
    [Fact]
    public void ConfigurationChangesCommitAtomicallyAndIgnoreOtherClients()
    {
        // Arrange
        bool? previousGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
        var previous = ConfigModSystem.Config.Postprocessing;
        try
        {
            ConfigModSystem.Config.Postprocessing = new PostprocessSettings
            {
                BloomStrength = 0, LightShaftStrength = 0, LightShaftLimit = 1,
                LocalExposure = new() { Enabled = false }
            };
            var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
            api.SetupGet(value => value.Settings.Bool["bloom"]).Returns(true);
            api.SetupGet(value => value.Settings.Int["godRays"]).Returns(2);
            api.SetupGet(value => value.ModLoader.Systems).Returns(Array.Empty<ModSystem>());

            // Act
            using var enablement = new PipelineEnablement(api.Object);
            var radiance = new List<bool>();
            using var subscription = enablement.LightShaftRadiance.Subscribe(radiance.Add);
            // Assert construction uses the disabled committed configuration.
            Assert.False(enablement.Bloom.Value);
            Assert.False(enablement.LocalExposure.Value);
            var config = ConfigModSystem.Config.Postprocessing;

            // Act: mutate source settings without publishing a configuration reload.
            config.BloomStrength = 1;
            config.LightShaftStrength = 1;
            config.LightShaftLimit = 0;
            config.LocalExposure.Enabled = true;
            // Assert values remain on the previously committed snapshot.
            Assert.False(enablement.Bloom.Value);
            var other = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
            other.SetupGet(value => value.ModLoader.Systems).Returns(Array.Empty<ModSystem>());

            // Act: notify a different client, then the owning client.
            LiveConfigReload.NotifyAll(other.Object);
            // Assert the unrelated notification is ignored.
            Assert.False(enablement.Bloom.Value);
            // Act and assert the owning-client reload publishes sanitized settings together.
            LiveConfigReload.NotifyAll(api.Object);
            Assert.True(enablement.Bloom.Value);
            Assert.True(enablement.LocalExposure.Value);
            Assert.Equal(new[] { false }, radiance);

            // Act: change the configured light-shaft limit and publish that settings snapshot.
            config.LightShaftLimit = 1;
            LiveConfigReload.NotifyAll(api.Object);
            // Assert the radiance condition follows the newly committed limit.
            Assert.True(enablement.LightShaftRadiance.Value);
            Assert.Equal(new[] { false, true }, radiance);

            // Act: dispose before one more reload notification.
            enablement.Dispose();
            config.LightShaftLimit = 0;
            LiveConfigReload.NotifyAll(api.Object);
            // Assert disposed subscriptions cannot publish additional values.
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
