using System.Reflection;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises actual rendering composition callbacks and shutdown owners through the engine service boundary.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ClientRenderingCompositionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>An intervening asset epoch cannot leave configuration reload permanently blocked by an obsolete queued callback.</summary>
    [Fact]
    public void ObsoleteConfigurationCallbackDoesNotBlockNextQueue()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        api.SetupGet(x => x.Assets).Returns(assets.Api.Assets);
        api.SetupGet(x => x.Logger).Returns(assets.Api.Logger);
        api.SetupGet(x => x.World.Player).Returns(RuntimeEngineServices.CameraPlayer(() => new VanillaGraphicsExpanded.LumOn.LumOnCameraState(0, 0, 0, 0, 0, 0, 0)));
        List<Action> tasks = [];
        api.Setup(x => x.Event.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((callback, _) => tasks.Add(callback));
        var registry = GpuShaderProgramLibraries.Create(api.Object);
        var lifetime = ClientRenderingLifetime.Find(api.Object)!;
        var root = new VanillaGraphicsExpandedModSystem();
        Set(root, "capi", api.Object); Set(root, "renderingLifetime", lifetime);
        Set(root, "pendingShaderReload", true);
        lifetime.Attach(registry.Dispose);
        try
        {
            Invoke(root, "TryReloadShadersInWorld"); Assert.Single(tasks);
            ClientRenderingLifetime.BeginNativeReload(); ClientRenderingLifetime.CompleteNativeReload();
            tasks[0]();
            api.Verify(x => x.Shader.ReloadShaders(), Times.Never);
            Invoke(root, "TryReloadShadersInWorld"); Assert.Equal(2, tasks.Count);
            tasks[1]();
            api.Verify(x => x.Shader.ReloadShaders(), Times.Once);
        }
        finally { lifetime.Shutdown(); }
    }

    /// <summary>An early engine manager callback preserves root ownership; root shutdown continues through a feature failure and drains native names last.</summary>
    [Fact]
    public void RootOwnsShutdownDespiteEarlyManagerCallback()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        api.SetupGet(x => x.Assets).Returns(assets.Api.Assets);
        api.SetupGet(x => x.Logger).Returns(assets.Api.Logger);
        var mods = new Mock<IModLoader>(); api.SetupGet(x => x.ModLoader).Returns(mods.Object);
        var root = new VanillaGraphicsExpandedModSystem(); var pbr = new PbrModSystem();
        var managerSystem = new RHIResourceManagerModSystem();
        mods.Setup(x => x.GetModSystem<VanillaGraphicsExpandedModSystem>(It.IsAny<bool>())).Returns(root);
        mods.Setup(x => x.GetModSystem<PbrModSystem>(It.IsAny<bool>())).Returns(pbr);
        mods.Setup(x => x.GetModSystem<RHIResourceManagerModSystem>(It.IsAny<bool>())).Returns(managerSystem);
        var registry = GpuShaderProgramLibraries.Create(api.Object);
        var lifetime = ClientRenderingLifetime.Find(api.Object)!;
        var manager = new RHIResourceManager(); RHIResourceManagerSystem.Initialize(manager);
        manager.OnRenderFrame(0, RHIResourceManager.Stage);
        Set(root, "capi", api.Object); Set(root, "renderingLifetime", lifetime);
        Set(managerSystem, "capi", api.Object); Set(managerSystem, "renderer", manager); Set(managerSystem, "rendererRegistered", true);
        managerSystem.AdoptByRenderingRoot();
        Set(pbr, "capi", api.Object);
        var depth = new DepthHierarchyRenderer(api.Object);
        var compute = DepthHierarchyComputeShader.Create(api.Object); int computeId = compute.ProgramId;
        Set(depth, "compute", compute); Set(pbr, "depthHierarchy", depth);
        List<string> order = [];
        api.Setup(x => x.Event.UnregisterRenderer(It.IsAny<IRenderer>(), It.IsAny<EnumRenderStage>()))
            .Callback<IRenderer, EnumRenderStage>((owner, _) =>
            {
                if (ReferenceEquals(owner, pbr)) { order.Add("feature-failure"); throw new InvalidOperationException("injected unregister failure"); }
                if (ReferenceEquals(owner, depth)) { Assert.False(registry.IsClosed); order.Add("depth"); }
                if (ReferenceEquals(owner, manager)) { Assert.True(registry.IsClosed); Assert.True(((IGpuProgram)compute).Lifetime.IsRetired); order.Add("manager"); }
            });
        lifetime.Attach(() => Invoke(root, "DisposeRendering"));
        var queued = NativeResourceSetup.CreateProgram(); int queuedId = queued.ProgramId;
        var thread = new Thread(queued.Dispose); thread.Start(); thread.Join();
        try
        {
            managerSystem.Dispose();
            Assert.False(lifetime.IsClosed); Assert.False(manager.IsDisposed);
            Assert.True(GL.IsProgram(computeId)); Assert.True(GL.IsProgram(queuedId));
            Assert.Empty(order);
            mods.Verify(x => x.GetModSystem<VanillaGraphicsExpandedModSystem>(It.IsAny<bool>()), Times.Never);
            Assert.Throws<AggregateException>(root.Dispose);
            Assert.True(lifetime.IsClosed); Assert.True(registry.IsClosed);
            Assert.False(GL.IsProgram(computeId)); Assert.False(GL.IsProgram(queuedId));
            Assert.True(order.IndexOf("feature-failure") < order.IndexOf("depth"));
            Assert.True(order.IndexOf("depth") < order.IndexOf("manager"));
            managerSystem.Dispose(); root.Dispose(); pbr.Dispose();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { lifetime.Shutdown(); manager.Dispose(); RHIResourceManagerSystem.Shutdown(); }
    }
    #endregion

    #region Private
    /// <summary>Installs concrete owners without running unrelated UI and Harmony startup.</summary>
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
    /// <summary>Invokes the actual composition action while preserving its original failure type.</summary>
    private static void Invoke(object owner, string method)
    {
        try { owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null); }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException).Throw(); }
    }
    #endregion
}
