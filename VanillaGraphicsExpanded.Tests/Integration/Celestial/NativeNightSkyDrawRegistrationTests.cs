using System.Runtime.CompilerServices;
using System.Reflection;
using Moq;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Celestial;

/// <summary>Exercises callback suppression against the installed native night-sky system.</summary>
public sealed class NativeNightSkyDrawRegistrationTests
{
    #region Public API
    /// <summary>Removes native draws while preserving unrelated callbacks, system identity and native resources.</summary>
    [Theory]
    [InlineData("SystemRenderNightSky", "OnRenderFrame3D")]
    [InlineData("SystemRenderSunMoon", "OnRenderFrame3D")]
    [InlineData("SystemRenderSunMoon", "OnRenderFrame3DPost")]
    public void DisableWithdrawsOnlyNativeDrawCallbacksAndPreservesNativeSystem(string system, string method)
    {
        // Arrange: install one native callback in each sky stage alongside an unrelated renderer.
        using var dependencies = new EngineDependencyResolution();
        var game = (ClientMain)RuntimeHelpers.GetUninitializedObject(typeof(ClientMain));
        GC.SuppressFinalize(game);
        var platform = new Mock<ClientPlatformAbstract>();
        game.Platform = platform.Object;
        var events = (ClientEventManager)RuntimeHelpers.GetUninitializedObject(typeof(ClientEventManager));
        events.renderersByStage = Enumerable.Range(0, Enum.GetValues<EnumRenderStage>().Max(stage => (int)stage) + 1)
            .Select(_ => new List<RenderHandler>()).ToArray();
        game.eventManager = events;
        var nativeType = typeof(ClientMain).Assembly.GetType("Vintagestory.Client.NoObf." + system, true)!;
        var native = (ClientSystem)RuntimeHelpers.GetUninitializedObject(nativeType);
        var callback = (Action<float>)nativeType.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Action<float>), native);
        var unrelated = new DummyRenderer { action = _ => { }, RenderOrder = .3 };
        var opaque = new DummyRenderer { action = callback, RenderOrder = .1 };
        var before = new DummyRenderer { action = callback, RenderOrder = .1 };
        events.renderersByStage[(int)EnumRenderStage.Opaque].Add(new() { Renderer = unrelated });
        events.renderersByStage[(int)EnumRenderStage.Opaque].Add(new() { Renderer = opaque });
        events.renderersByStage[(int)EnumRenderStage.Before].Add(new() { Renderer = before });
        game.clientSystems = [native];
        var originalSystems = game.clientSystems;
        var api = new Mock<ICoreClientAPI>();
        api.SetupGet(value => value.World).Returns(game);

        // Act: suppress only the installed native night-sky draw callbacks.
        NativeNightSkyDrawRegistration.Disable(api.Object);
        // Assert: the native client system and unrelated opaque draw remain registered.
        Assert.Same(originalSystems, game.clientSystems);
        Assert.Same(native, Assert.Single(game.clientSystems));
        Assert.Same(unrelated, Assert.Single(events.renderersByStage[(int)EnumRenderStage.Opaque]).Renderer);
        Assert.Empty(events.renderersByStage[(int)EnumRenderStage.Before]);

        // Act: repeat suppression to check the operation is idempotent.
        NativeNightSkyDrawRegistration.Disable(api.Object);
        // Assert: native ownership, unrelated draws, and mesh/texture lifetimes remain intact.
        Assert.Same(originalSystems, game.clientSystems);
        Assert.Same(native, Assert.Single(game.clientSystems));
        Assert.Same(unrelated, Assert.Single(events.renderersByStage[(int)EnumRenderStage.Opaque]).Renderer);
        Assert.DoesNotContain(events.renderersByStage.SelectMany(stage => stage),
            handler => handler.Renderer is DummyRenderer wrapper && ReferenceEquals(wrapper.action.Target, native));
        platform.Verify(value => value.DeleteMesh(It.IsAny<MeshRef>()), Times.Never);
        platform.Verify(value => value.GLDeleteTexture(It.IsAny<int>()), Times.Never);
    }
    #endregion
}
