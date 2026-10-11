using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Moq;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies shared world-camera publication timing, history alignment and lifetime withdrawal.</summary>
[Collection("GPU")]
public sealed class VgeFrameRendererTests
{
    #region Public API
    /// <summary>The post-camera Before callback publishes once per frame; early reset and world retirement withdraw it.</summary>
    [Fact]
    public void PublicationFollowsWorldRenderLifetime()
    {
        var api = CreateApi(out _, out _);
        using var renderer = new VgeFrameRenderer(api.Object);
        Assert.Equal(.001, renderer.RenderOrder);
        api.Verify(a => a.Event.RegisterRenderer(renderer, EnumRenderStage.Before, "vge_frame_camera"), Times.Once);
        api.Verify(a => a.Event.RegisterRenderer(It.Is<VgeFrameResetRenderer>(reset => reset.RenderOrder == -10001), EnumRenderStage.Before, "vge_frame_reset"), Times.Once);
        Assert.Throws<InvalidOperationException>(() => VgeFrameRenderer.Current);
        renderer.BeginFrame();
        Assert.Throws<InvalidOperationException>(() => VgeFrameRenderer.Current);
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        byte[] published = VgeFrameRenderer.Current.Bytes.ToArray();
        Assert.Equal(1u, MemoryMarshal.Read<uint>(published.AsSpan(396)));
        Assert.Equal(new float[] { .125f, 2048, .016f }, MemoryMarshal.Cast<byte, float>(published.AsSpan(496, 12)).ToArray());
        Assert.Equal(1u, MemoryMarshal.Read<uint>(published.AsSpan(508)));
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        Assert.Equal(published, VgeFrameRenderer.Current.Bytes.ToArray());
        renderer.BeginFrame();
        Assert.Throws<InvalidOperationException>(() => VgeFrameRenderer.Current);
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        Mock.Get(api.Object.Event).Raise(e => e.LeaveWorld += null);
        Assert.Throws<InvalidOperationException>(() => VgeFrameRenderer.Current);
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        Assert.Equal(VgeFrameRenderer.Current.Bytes[..64].ToArray(), VgeFrameRenderer.Current.Bytes.Slice(256, 64).ToArray());
        renderer.Dispose();
        Assert.Throws<InvalidOperationException>(() => VgeFrameRenderer.Current);
        api.Verify(a => a.Event.UnregisterRenderer(renderer, EnumRenderStage.Before), Times.Once);
        api.Verify(a => a.Event.UnregisterRenderer(It.IsAny<VgeFrameResetRenderer>(), EnumRenderStage.Before), Times.Once);
    }

    /// <summary>Repeated publication calls retain one camera generation until the next early frame reset.</summary>
    [Fact]
    public void DuplicateOpaqueCallbackDoesNotAdvanceCameraHistory()
    {
        var api = CreateApi(out var entity, out var projection);
        using var renderer = new VgeFrameRenderer(api.Object);
        renderer.BeginFrame();
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        byte[] first = VgeFrameRenderer.Current.Bytes.ToArray();
        projection[0] = 2;
        entity.CameraPos.X += .25;
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        Assert.Equal(first, VgeFrameRenderer.Current.Bytes.ToArray());
        renderer.BeginFrame();
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        Assert.Equal(2f, MemoryMarshal.Read<float>(VgeFrameRenderer.Current.Bytes));
        Assert.Equal(2u, MemoryMarshal.Read<uint>(VgeFrameRenderer.Current.Bytes[396..]));
    }

    /// <summary>Previous clip transforms include the small current-origin displacement before world coordinates lose float precision.</summary>
    [Fact]
    public void PreviousViewProjectionAlignsLargeWorldOrigins()
    {
        var api = CreateApi(out var entity, out _);
        entity.CameraPos.Set(16_777_216.25, 32, -16_777_216.25);
        using var renderer = new VgeFrameRenderer(api.Object);
        renderer.BeginFrame();
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        entity.CameraPos.Set(16_777_216.5, 32.5, -16_777_216.125);
        renderer.BeginFrame();
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        float[] previous = MemoryMarshal.Cast<byte, float>(VgeFrameRenderer.Current.Bytes.Slice(256, 64)).ToArray();
        Assert.Equal(new float[] { .25f, .5f, .125f, 1 }, previous[12..]);
        Assert.Equal(new float[] { 1, 0, 0, 0 }, previous[..4]);
        Assert.Equal(new int[] { 524288, 1, -524289, 0 },
            MemoryMarshal.Cast<byte, int>(VgeFrameRenderer.Current.Bytes.Slice(512, 16)).ToArray());
        Assert.Equal(new float[] { .5f, .5f, 31.875f, 0 },
            MemoryMarshal.Cast<byte, float>(VgeFrameRenderer.Current.Bytes.Slice(528, 16)).ToArray());
    }
    /// <summary>A teleport withdraws matrix history for one camera generation while ordinary motion retains it.</summary>
    [Fact]
    public void CameraCutPublishesCurrentTransformAsPrevious()
    {
        var api = CreateApi(out var entity, out var projection);
        using var renderer = new VgeFrameRenderer(api.Object);
        renderer.BeginFrame();
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        entity.CameraPos.X = 1;
        renderer.BeginFrame();
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        Assert.Equal(0u, MemoryMarshal.Read<uint>(VgeFrameRenderer.Current.Bytes[508..]));
        projection[0] = 2;
        entity.CameraPos.X = 100;
        renderer.BeginFrame();
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        Assert.Equal(1u, MemoryMarshal.Read<uint>(VgeFrameRenderer.Current.Bytes[508..]));
        Assert.Equal(VgeFrameRenderer.Current.Bytes.Slice(320, 64).ToArray(),
            VgeFrameRenderer.Current.Bytes.Slice(256, 64).ToArray());
    }
    /// <summary>Engine scratch aliasing and later shadow matrices cannot replace the captured world projection or view.</summary>
    [Fact]
    public void BeforePublicationCopiesScratchAndDedicatedWorldView()
    {
        var api = CreateApi(out _, out var projection);
        projection[0] = 2;
        float[] dedicatedView = Mat4f.Create(); dedicatedView[12] = 7;
        float[] activePassView = Mat4f.Create(); activePassView[0] = 9;
        float[] scratch = new float[16];
        api.SetupGet(value => value.Render.CurrentProjectionMatrix).Returns(() =>
        {
            projection.CopyTo(scratch, 0);
            return scratch;
        });
        api.SetupGet(value => value.Render.CurrentModelviewMatrix).Returns(() =>
        {
            activePassView.CopyTo(scratch, 0);
            return scratch;
        });
        api.SetupGet(value => value.Render.CameraMatrixOriginf).Returns(dedicatedView);
        // A subsequent render helper can overwrite the engine's shared matrix scratch array.
        api.SetupGet(value => value.Render.FogColor).Returns(() =>
        {
            _ = api.Object.Render.CurrentModelviewMatrix;
            return new Vec4f(.1f, .2f, .3f, 1);
        });
        using var renderer = new VgeFrameRenderer(api.Object);
        renderer.BeginFrame();
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        byte[] first = VgeFrameRenderer.Current.Bytes.ToArray();
        Assert.Equal(projection, VgeFrameRenderer.Current.Projection.ToArray());
        Assert.Equal(dedicatedView, VgeFrameRenderer.Current.View.ToArray());
        projection[0] = 4;
        dedicatedView[12] = 11;
        renderer.OnRenderFrame(.016f, EnumRenderStage.Opaque);
        renderer.OnRenderFrame(.016f, EnumRenderStage.Before);
        Assert.Equal(first, VgeFrameRenderer.Current.Bytes.ToArray());
        renderer.BeginFrame();
        Assert.Throws<InvalidOperationException>(() => VgeFrameRenderer.Current);
        renderer.OnRenderFrame(.032f, EnumRenderStage.Before);
        Assert.Equal(2u, MemoryMarshal.Read<uint>(VgeFrameRenderer.Current.Bytes[396..]));
        Assert.Equal(first.AsSpan(320, 64).ToArray(), VgeFrameRenderer.Current.Bytes.Slice(256, 64).ToArray());
        Assert.Equal(projection, VgeFrameRenderer.Current.Projection.ToArray());
        Assert.Equal(dedicatedView, VgeFrameRenderer.Current.View.ToArray());
    }
    #endregion

    #region Private
    /// <summary>Provides controlled engine camera, dimensions, clock and fog without creating a graphics context.</summary>
    private static Mock<ICoreClientAPI> CreateApi(out EntityPlayer entity, out float[] projection)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        entity = new EntityPlayer();
        projection = Mat4f.Create();
        // The installed player interface cannot be proxied by Castle; retain the existing engine fixture convention.
        var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var worldData = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        typeof(ClientWorldPlayerData).GetField("entityplayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(worldData, entity);
        typeof(ClientPlayer).GetField("worlddata", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(player, worldData);
        api.SetupGet(a => a.World.Player).Returns(player);
        api.SetupGet(a => a.Render.CurrentProjectionMatrix).Returns(projection);
        api.SetupGet(a => a.Render.CameraMatrixOriginf).Returns(Mat4f.Create());
        api.SetupGet(a => a.Render.FrameWidth).Returns(1920);
        api.SetupGet(a => a.Render.FrameHeight).Returns(1080);
        api.SetupGet(a => a.Render.FogColor).Returns(new Vec4f(.1f, .2f, .3f, 1));
        api.SetupGet(a => a.Render.FogDensity).Returns(.2f);
        api.SetupGet(a => a.Render.ShaderUniforms).Returns(new DefaultShaderUniforms { ZNear = .125f, ZFar = 2048 });
        return api;
    }
    #endregion
}
