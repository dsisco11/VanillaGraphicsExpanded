using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using FluffyClouds;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Tests.Component.Clouds;

    /// <summary>Checks native draw ownership gates follow live settings without changing registrations.</summary>
    [Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CloudNativeAdapterTests(HeadlessGLFixture fixture)
{
    #region Public API


/// <summary>Prepared ownership gates only the current native draw, follows the live setting and leaves every native registration intact.</summary>
    [Fact]
    public void DrawGateFollowsCurrentModeWithoutRegistrationChanges()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        fixture.EnsureContextValid();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        // Arrange: configure mock event and settings responses before initializing the native runtime.
        var events = new Mock<IClientEventAPI>(); api.SetupGet(value => value.Event).Returns(events.Object);
        var system = new FluffyCloudsModSystem();
        var nativeDraw = (CloudRendererVolumetric)RuntimeHelpers.GetUninitializedObject(typeof(CloudRendererVolumetric));
        AccessTools.Field(typeof(FluffyCloudsModSystem), "volumetric").SetValue(system, nativeDraw);
        api.Setup(value => value.ModLoader.GetModSystem<FluffyCloudsModSystem>(It.IsAny<bool>())).Returns(system);
        int mode = 1; api.Setup(value => value.Settings.Int.Get("cloudRenderMode", 0)).Returns(() => mode);
        using var runtime = new CloudNativeRuntime(api.Object);
        // Act: initialize the live native cloud mode and its runtime gate.
        events.Raise(value => value.LevelFinalize += null); runtime.Update();
        var resources = new Mock<IDisposable>();
        // Assert: activation and mode changes affect only the intended native draw and dispose ownership once.
        Assert.True(runtime.Ownership.TryActivate(CloudMode.Volumetric, resources.Object, true));
        Assert.True(runtime.SuppressVolume(nativeDraw));
        Assert.True(runtime.SuppressVolume(nativeDraw)); // A native watcher may re-register the same draw freely.
        var foreign = (CloudRendererVolumetric)RuntimeHelpers.GetUninitializedObject(typeof(CloudRendererVolumetric));
        Assert.False(runtime.SuppressVolume(foreign));
        mode = 2; Assert.False(runtime.SuppressVolume(nativeDraw));
        resources.Verify(value => value.Dispose(), Times.Once);
        events.Verify(value => value.RegisterRenderer(nativeDraw, EnumRenderStage.OIT, "cloudsvolumetric"), Times.Never);
        mode = 1; runtime.Update();
        var replacement = new Mock<IDisposable>();
        Assert.True(runtime.Ownership.TryActivate(CloudMode.Volumetric, replacement.Object, true));
        runtime.Ownership.Dispose();
        Assert.False(runtime.SuppressVolume(nativeDraw));
        replacement.Verify(value => value.Dispose(), Times.Once);
        events.Verify(value => value.RegisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>(), It.IsAny<string>()), Times.Never);
        events.Verify(value => value.UnregisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>()), Times.Never);
        api.VerifySet(value => value.Settings.Int["cloudRenderMode"] = It.IsAny<int>(), Times.Never);
    }

/// <summary>The independent volume callback never advances native weather and retains ownership across unavailable maps and reload.</summary>
    [Fact]
    public void StoredRendererRetainsItsGateUntilCurrentModeRelease()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        fixture.EnsureContextValid();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        // Arrange: configure mock event and settings responses before initializing the native runtime.
        var events = new Mock<IClientEventAPI>(); api.SetupGet(value => value.Event).Returns(events.Object);
        var system = new FluffyCloudsModSystem();
        var nativeDraw = (CloudRendererVolumetric)RuntimeHelpers.GetUninitializedObject(typeof(CloudRendererVolumetric));
        AccessTools.Field(typeof(FluffyCloudsModSystem), "volumetric").SetValue(system, nativeDraw);
        api.Setup(value => value.ModLoader.GetModSystem<FluffyCloudsModSystem>(It.IsAny<bool>())).Returns(system);
        int mode = 1; api.Setup(value => value.Settings.Int.Get("cloudRenderMode", 0)).Returns(() => mode);
        // Act: initialize native and stored renderers, then exercise callback and reload lifecycle transitions.
        using var runtime = new CloudNativeRuntime(api.Object);
        events.Raise(value => value.LevelFinalize += null); runtime.Update();
        var renderer = new VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudStoredVolumeConsumer(api.Object, runtime,()=>VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeViewSettings.Default);
        AccessTools.Field(typeof(CloudNativeRuntime), "volume").SetValue(runtime, renderer);
        // Assert: the stored owner preserves its gate until mode release and unregisters only itself.
        Assert.Equal(.31, renderer.RenderOrder);
        // Executable arithmetic is exercised separately by the real binary GPU fixtures.
        Assert.True(runtime.Ownership.TryActivate(CloudMode.Volumetric, renderer, true));
        api.Object.Event.RegisterRenderer(renderer, EnumRenderStage.OIT, "vge_cloud_volume");
        renderer.OnRenderFrame(.1f, EnumRenderStage.OIT);
        Assert.True(runtime.SuppressVolume(nativeDraw)); Assert.False(runtime.TryGetMap(out _));
        events.Raise(value => value.ReloadShader += null);
        Assert.True(runtime.SuppressVolume(nativeDraw));
        mode = 0; renderer.OnRenderFrame(.1f, EnumRenderStage.OIT);
        Assert.False(runtime.SuppressVolume(nativeDraw));
        renderer.Dispose();
        events.Verify(value => value.UnregisterRenderer(renderer, EnumRenderStage.OIT), Times.Once);
        events.Verify(value => value.UnregisterRenderer(nativeDraw, It.IsAny<EnumRenderStage>()), Times.Never);
        api.VerifySet(value => value.Settings.Int["cloudRenderMode"] = It.IsAny<int>(), Times.Never);
    }
    #endregion
}
