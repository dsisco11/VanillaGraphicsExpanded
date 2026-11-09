using Moq;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Qualifies change-driven hierarchy demand from settings and committed consumer lifetimes.</summary>
[Collection("CPU state")]
public sealed class DepthHierarchyEnablementTests
{
    #region Public API
    /// <summary>Settings and frozen lighting changes update demand without a render-frame settings poll.</summary>
    [Fact]
    public void ConsumerChangesUpdateEnablementAndUnsubscribeOnDisposal()
    {
        // Arrange
        bool? previous = PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled = false;
            var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
            var settings = new Mock<ISettingsClass<int>>();
            api.SetupGet(value => value.Settings.Int).Returns(settings.Object);
            int quality = 0;
            settings.SetupGet(value => value["ssaoQuality"]).Returns(() => quality);
            OnSettingsChanged<int>? changed = null;
            settings.Setup(value => value.AddWatcher("ssaoQuality", It.IsAny<OnSettingsChanged<int>>()))
                .Callback<string, OnSettingsChanged<int>>((_, callback) => changed = callback);
            using var renderer = new DepthHierarchyRenderer(api.Object);
            using var enablement = new DepthHierarchyEnablement(api.Object, renderer);
            Assert.NotNull(changed); Assert.False(renderer.Enabled);
            settings.Invocations.Clear();

            // Act: deliver frames while checking that settings are not polled in the render loop.
            for (int frame = 0; frame < 4; frame++)
            {
                renderer.OnRenderFrame(0, EnumRenderStage.Before);
                renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
            }

            // Assert the frame callbacks did not query the setting.
            settings.VerifyGet(value => value["ssaoQuality"], Times.Never);

            // Act / Assert the registered settings callback updates demand and is removed on disposal.
            quality = 1; changed!(quality); Assert.True(renderer.Enabled);
            PbrShaderLightingMode.GenerationLumOnEnabled = true;
            quality = 0; changed(quality); Assert.True(renderer.Enabled);
            PbrShaderLightingMode.GenerationLumOnEnabled = false; Assert.False(renderer.Enabled);
            enablement.Dispose();
            settings.Verify(value => value.RemoveWatcher("ssaoQuality", changed), Times.Once);
            PbrShaderLightingMode.GenerationLumOnEnabled = true; Assert.False(renderer.Enabled);
            quality = 1; changed(quality); Assert.False(renderer.Enabled);
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled = previous; }
    }

    /// <summary>Cloud preferences alone do not enable storage; admitted volume ownership and release do.</summary>
    [Fact]
    public void ActualVolumeOwnershipControlsDemand()
    {
        // Arrange
        bool? previous = PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled = false;
            var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
            api.SetupGet(value => value.Settings.Int["ssaoQuality"]).Returns(0);
            api.SetupGet(value => value.Settings.Int["cloudRenderMode"]).Returns(1);
            using var runtime = new CloudNativeRuntime(api.Object);
            using var renderer = new DepthHierarchyRenderer(api.Object);

            // Act / Assert: only committed volumetric ownership should create hierarchy demand.
            using var enablement = new DepthHierarchyEnablement(api.Object, renderer);
            runtime.Ownership.Update(true, 1); Assert.False(renderer.Enabled);
            Assert.False(runtime.Ownership.TryActivate(CloudMode.Volumetric, new Resource(), false));
            Assert.False(renderer.Enabled);
            Assert.True(runtime.Ownership.TryActivate(CloudMode.Volumetric, new Resource(), true));
            Assert.True(renderer.Enabled);
            runtime.Ownership.Update(true, 2); Assert.False(renderer.Enabled);
            Assert.True(runtime.Ownership.TryActivate(CloudMode.Simple, new Resource(), true));
            Assert.False(renderer.Enabled);
            runtime.Ownership.Dispose();
            runtime.Ownership.Update(true, 1);
            Assert.True(runtime.Ownership.TryActivate(CloudMode.Volumetric, new Resource(), true));
            Assert.True(renderer.Enabled);
            runtime.Dispose(); Assert.False(renderer.Enabled);
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled = previous; }
    }
    #endregion

    #region Private
    /// <summary>Represents a functioning admitted draw without allocating GPU storage.</summary>
    private sealed class Resource : IDisposable
    {
        #region Public API
        /// <summary>Releases the empty controlled draw lifetime.</summary>
        public void Dispose() { }
        #endregion
    }
    #endregion
}
