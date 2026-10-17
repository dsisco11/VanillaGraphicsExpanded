using System.Runtime.CompilerServices;
using FluffyClouds;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Qualifies cloud lifetime transitions through native registration and VGE callback admission.</summary>
[Collection("GPU")]
public sealed class CloudLifecycleTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Enumerates every ordered native mode pair under both receiver-lighting selections.</summary>
    public static IEnumerable<object[]> Transitions()
    {
        foreach (bool lumOn in new[] { false, true })
        for (int source = 0; source < 3; source++)
        for (int destination = 0; destination < 3; destination++)
            yield return [lumOn, source, destination];
    }

    /// <summary>Repeated transitions select one draw gate, tolerate watcher registration, and restore the live preference on disable.</summary>
    [Theory]
    [MemberData(nameof(Transitions))]
    public void NativeWatcherAndDisableRestoreCurrentMode(bool lumOn, int source, int destination)
    {
        fixture.EnsureContextValid();
        bool? previous = PbrShaderLightingMode.GenerationLumOnEnabled;
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var events = new Mock<IClientEventAPI>(); api.SetupGet(value => value.Event).Returns(events.Object);
        var system = new FluffyCloudsModSystem();
        var weather = (WeatherSystemClient)RuntimeHelpers.GetUninitializedObject(typeof(WeatherSystemClient));
        var nativeVolume = (CloudRendererVolumetric)RuntimeHelpers.GetUninitializedObject(typeof(CloudRendererVolumetric));
        var nativeSimple = (CloudRenderer)RuntimeHelpers.GetUninitializedObject(typeof(CloudRenderer));
        AccessTools.Field(typeof(FluffyCloudsModSystem), "capi").SetValue(system, api.Object);
        AccessTools.Field(typeof(FluffyCloudsModSystem), "volumetric").SetValue(system, nativeVolume);
        AccessTools.Field(typeof(WeatherSystemClient), "cloudRenderer").SetValue(weather, nativeSimple);
        api.Setup(value => value.ModLoader.GetModSystem<FluffyCloudsModSystem>(It.IsAny<bool>())).Returns(system);
        api.Setup(value => value.ModLoader.GetModSystem<WeatherSystemClient>(It.IsAny<bool>())).Returns(weather);
        int mode = source; api.Setup(value => value.Settings.Int.Get("cloudRenderMode", 0)).Returns(() => mode);
        var registered = new HashSet<IRenderer>();
        events.Setup(value => value.RegisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.OIT, It.IsAny<string>()))
            .Callback<IRenderer, EnumRenderStage, string>((renderer, _, _) => registered.Add(renderer));
        events.Setup(value => value.UnregisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.OIT))
            .Callback<IRenderer, EnumRenderStage>((renderer, _) => registered.Remove(renderer));
        var watcher = AccessTools.Method(typeof(FluffyCloudsModSystem), "registerCloudRenderer");
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled = lumOn;
            // A new runtime is the enable/re-enable boundary. Native roots and current
            // preferences survive its disposal; no weather worker or native GPU owner is created.
            for (int cycle = 0; cycle < 3; cycle++)
            {
                using var runtime = new CloudNativeRuntime(api.Object);
                events.Raise(value => value.LevelFinalize += null); runtime.Update();
                foreach (int next in new[] { source, destination, destination })
                {
                    mode = next; watcher.Invoke(system, [mode]); runtime.Update();
                    Admit(api.Object, runtime, mode);
                    watcher.Invoke(system, [mode]); // Native watcher may register again after ownership.
                    bool volumeSuppressed = runtime.SuppressVolume(nativeVolume);
                    bool simpleSuppressed = runtime.RenderSimple(nativeSimple, .125f, EnumRenderStage.OIT);
                    Assert.Equal(mode == 1, volumeSuppressed);
                    Assert.Equal(mode == 2, simpleSuppressed);
                    Assert.False(volumeSuppressed && simpleSuppressed);
                    Assert.Equal(mode == 1, registered.Contains(nativeVolume));
                    Assert.Equal(mode == 1, registered.Any(renderer => renderer is CloudVolumeRenderer));
                    Assert.Equal(mode == 2, AccessTools.Field(typeof(CloudNativeRuntime), "simple").GetValue(runtime) is not null);
                    Assert.Equal(mode == 1, AccessTools.Field(typeof(CloudNativeRuntime), "volume").GetValue(runtime) is not null);
                    object? consumer = AccessTools.Field(typeof(CloudNativeRuntime), mode == 1 ? "volume" : "simple").GetValue(runtime);
                    if (consumer is not null)
                    {
                        AccessTools.Field(consumer.GetType(), "failed").SetValue(consumer, true);
                        runtime.PrepareSimple(); runtime.PrepareVolume();
                        Assert.True(runtime.Ownership.Owns((CloudMode)mode));
                        Assert.True((bool)AccessTools.Field(consumer.GetType(), "failed").GetValue(consumer)!);
                    }
                    // Reload withdraws readiness without resurrecting a retired mode or native draw.
                    events.Raise(value => value.ReloadShader += null);
                    Assert.Equal(mode == 1, runtime.SuppressVolume(nativeVolume));
                    if (consumer is not null) Assert.False((bool)AccessTools.Field(consumer.GetType(), "failed").GetValue(consumer)!);
                }
                runtime.Dispose(); runtime.Dispose();
                Assert.Null(CloudNativeRuntime.Active);
                Assert.False(runtime.SuppressVolume(nativeVolume));
                Assert.False(runtime.RenderSimple(nativeSimple, .125f, EnumRenderStage.OIT));
                Assert.DoesNotContain(registered, renderer => renderer is CloudVolumeRenderer);
                Assert.Equal(mode == 1, registered.Contains(nativeVolume));
                Assert.Same(nativeVolume, AccessTools.Field(typeof(FluffyCloudsModSystem), "volumetric").GetValue(system));
                Assert.Same(nativeSimple, AccessTools.Field(typeof(WeatherSystemClient), "cloudRenderer").GetValue(weather));
                Assert.Equal(destination, mode);
            }
            api.VerifySet(value => value.Settings.Int["cloudRenderMode"] = It.IsAny<int>(), Times.Never);
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled = previous; }
    }

    /// <summary>A failing event manager cannot prevent owned renderer cleanup or leave a visible ownership gate.</summary>
    [Fact]
    public void CallbackRemovalFailureStillRetiresOwnedResources()
    {
        fixture.EnsureContextValid();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var events = new Mock<IClientEventAPI>(); api.SetupGet(value => value.Event).Returns(events.Object);
        api.Setup(value => value.Settings.Int.Get("cloudRenderMode", 0)).Returns(1);
        using var runtime = new CloudNativeRuntime(api.Object);
        events.Raise(value => value.LevelFinalize += null); runtime.Update();
        var renderer = new CloudVolumeRenderer(api.Object, runtime);
        var reconstruction = AccessTools.Field(typeof(CloudVolumeRenderer), "reconstruction").GetValue(renderer)!;
        var storage = new CloudVolumeStorage(1, 1, "Tests.Cloud.Teardown");
        var surface = storage.Blocks[0];
        int framebuffer = surface.Target.FboId;
        int[] textures = [storage.Array.TextureId,..storage.Blocks.Select(block=>block.View.TextureId)];
        AccessTools.Field(reconstruction.GetType(), "storage").SetValue(reconstruction, storage);
        Assert.True(runtime.Ownership.TryActivate(CloudMode.Volumetric, renderer, true));
        events.Setup(value => value.UnregisterRenderer(renderer, EnumRenderStage.OIT)).Throws(new InvalidOperationException("event teardown"));
        var error = Assert.Throws<AggregateException>(() => runtime.Ownership.Dispose());
        Assert.Contains(error.InnerExceptions, failure => failure.Message == "event teardown");
        Assert.False(runtime.Ownership.Owns(CloudMode.Volumetric));
        Assert.False(GL.IsFramebuffer(framebuffer));
        Assert.All(textures, texture => Assert.False(GL.IsTexture(texture)));
        Assert.Null(AccessTools.Field(reconstruction.GetType(), "storage").GetValue(reconstruction));
        renderer.Dispose(); // No second event removal or resource retirement.
        events.Verify(value => value.UnregisterRenderer(renderer, EnumRenderStage.OIT), Times.Once);
    }
    #endregion

    #region Private
    /// <summary>Transfers real draw lifetimes without shader preparation; executable behavior is qualified by separate GPU fixtures.</summary>
    private static void Admit(ICoreClientAPI api, CloudNativeRuntime runtime, int mode)
    {
        if (mode == 0 || runtime.Ownership.Owns((CloudMode)mode)) return;
        if (mode == 1)
        {
            var renderer = new CloudVolumeRenderer(api, runtime);
            Assert.True(runtime.Ownership.TryActivate(CloudMode.Volumetric, renderer, true));
            AccessTools.Field(typeof(CloudNativeRuntime), "volume").SetValue(runtime, renderer);
            api.Event.RegisterRenderer(renderer, EnumRenderStage.OIT, "vge_cloud_volume");
        }
        else
        {
            var renderer = new CloudSimpleRenderer(api);
            Assert.True(runtime.Ownership.TryActivate(CloudMode.Simple, renderer, true));
            AccessTools.Field(typeof(CloudNativeRuntime), "simple").SetValue(runtime, renderer);
        }
    }
    #endregion
}
