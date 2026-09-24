using Moq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Composes typed engine service mocks while leaving VGE initialization and renderer ownership intact.</summary>
internal static class RuntimeEngineServices
{
    #region Engine services
    /// <summary>Provides the headless render boundary with authored matrices and observed mesh submission.</summary>
    public static IRenderAPI Render(int edge, List<FrameBufferRef> framebuffers, Func<float[]> view,
        Func<float[]> projection, Action draw, DefaultShaderUniforms? uniforms = null)
    {
        var render = new Mock<IRenderAPI>(MockBehavior.Strict);
        render.SetupGet(api => api.FrameWidth).Returns(edge);
        render.SetupGet(api => api.FrameHeight).Returns(edge);
        render.SetupGet(api => api.CameraMatrixOriginf).Returns(view);
        render.SetupGet(api => api.CurrentProjectionMatrix).Returns(projection);
        render.SetupGet(api => api.FrameBuffers).Returns(framebuffers);
        render.SetupGet(api => api.FogColor).Returns(new Vec4f());
        render.SetupGet(api => api.FogDensity).Returns(0f);
        render.SetupGet(api => api.FogMin).Returns(0f);
        render.Setup(api => api.GLDepthMask(It.IsAny<bool>())).Callback((bool enabled) => OpenTK.Graphics.OpenGL.GL.DepthMask(enabled));
        render.SetupGet(api => api.AmbientColor).Returns(new Vec3f());
        render.SetupGet(api => api.ShaderUniforms).Returns(uniforms ?? new DefaultShaderUniforms { ZNear = .1f, ZFar = 100 });
        render.Setup(api => api.UploadMesh(It.IsAny<MeshData>())).Returns(() => new RuntimeMesh());
        render.Setup(api => api.DeleteMesh(It.IsAny<MeshRef>())).Callback((MeshRef mesh) => mesh.Dispose());
        render.Setup(api => api.RenderMesh(It.IsAny<MeshRef>())).Callback(draw);
        render.Setup(api => api.GlToggleBlend(It.IsAny<bool>(), It.IsAny<EnumBlendMode>()))
            .Callback((bool enabled, EnumBlendMode _) => ToggleBlend(enabled));
        return render.Object;
    }

    /// <summary>Exposes supplied services and forwards only asset/logging dependencies from the built-shader host.</summary>
    public static ICoreClientAPI Client(ICoreClientAPI assets, IClientEventAPI events, IClientWorldAccessor world,
        IRenderAPI render, IShaderAPI shader, IModLoader? mods = null, IInputAPI? input = null)
    {
        var api = new Mock<ICoreClientAPI>(MockBehavior.Strict);
        api.SetupGet(value => value.Assets).Returns(assets.Assets);
        api.SetupGet(value => value.Logger).Returns(assets.Logger);
        api.SetupGet(value => value.Side).Returns(EnumAppSide.Client);
        api.SetupGet(value => value.Event).Returns(events);
        api.As<ICoreAPI>().SetupGet(value => value.Event).Returns(events);
        api.As<ICoreAPI>().SetupGet(value => value.World).Returns(world);
        api.SetupGet(value => value.World).Returns(world);
        api.SetupGet(value => value.Render).Returns(render);
        api.SetupGet(value => value.Shader).Returns(shader);
        api.SetupGet(value => value.ModLoader).Returns(mods!);
        api.SetupGet(value => value.Input).Returns(input!);
        return api.Object;
    }

    /// <summary>Accepts game hotkey registration without introducing a window or keyboard device.</summary>
    public static IInputAPI Input()
    {
        var input = new Mock<IInputAPI>(MockBehavior.Strict);
        input.Setup(api => api.RegisterHotKey(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GlKeys>(),
            It.IsAny<HotkeyType>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()));
        input.Setup(api => api.SetHotKeyHandler(It.IsAny<string>(), It.IsAny<ActionConsumable<KeyCombination>>()));
        return input.Object;
    }
    /// <summary>Mirrors engine blend enablement; production rendering invalidates its own cached state where needed.</summary>
    private static void ToggleBlend(bool enabled)
    {
        if (enabled) OpenTK.Graphics.OpenGL.GL.Enable(OpenTK.Graphics.OpenGL.EnableCap.Blend);
        else OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.Blend);
    }
    /// <summary>Represents the engine mesh handle; the fixture draws its own fullscreen geometry.</summary>
    private sealed class RuntimeMesh : MeshRef { public override bool Initialized => !Disposed; }
    #endregion
}
