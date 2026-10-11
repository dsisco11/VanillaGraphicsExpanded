using System.Collections.Immutable;
using VanillaGraphicsExpanded.HarmonyPatches;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the owned sky through its runtime registration, framebuffer owner and independent fullscreen inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereSkyIntegrationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The enabled renderer draws without an engine sky shader and restores native graphics state.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentFullscreenDrawRestoresState(bool ssao)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var programs = new RuntimeLightingPrograms();
        using var atmosphere = new AtmosphereModSystem();
        using var material = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        using var glow = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba8);
        using var normal = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        using var position = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        using var depthStorage = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent32f);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var liquidDepth = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [1f, 1f, 1f, 1f]);
        using var engine = GpuFramebuffer.Create(ssao ? [material, glow, normal, position] : [material, glow], depth);
        var primary = new FrameBufferRef { FboId = engine.FboId, Width = 2, Height = 2, DepthTextureId = depth.TextureId,
            ColorTextureIds = ssao ? [material.TextureId, glow.TextureId, normal.TextureId, position.TextureId] : [material.TextureId, glow.TextureId] };
        var frames = Enumerable.Repeat<FrameBufferRef>(null!, 25).ToList();
        frames[(int)EnumFrameBuffer.Primary] = primary;
        frames[(int)EnumFrameBuffer.LiquidDepth] = new FrameBufferRef { DepthTextureId = liquidDepth.TextureId, Width = 2, Height = 2 };
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        var uniforms = new DefaultShaderUniforms { ZNear = .1f, ZFar = 100, WaterMurkColor = new Vec4f() };
        typeof(DefaultShaderUniforms).GetField("SkyDaylight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(uniforms, 1f);
        var render = RuntimeEngineServices.Render(2, frames, () => identity, () => identity, () => { }, uniforms);
        Mock.Get(render).SetupGet(value => value.CurrentModelviewMatrix).Returns(identity);
        Mock.Get(render).SetupGet(value => value.WireframeDebugRender).Returns(new WireframeModes());
        Mock.Get(render).Setup(value => value.DeleteMesh(It.IsAny<MeshRef>()));
        Mock.Get(render).SetupGet(value => value.CurrentRenderStage).Returns(EnumRenderStage.Opaque);
        Mock.Get(render).SetupGet(value => value.CurrentFrameBuffer).Returns(primary);
        var events = new Mock<IClientEventAPI>();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        api.SetupGet(value => value.Assets).Returns(assets.Api.Assets);
        api.SetupGet(value => value.Logger).Returns(assets.Api.Logger);
        api.SetupGet(value => value.Side).Returns(EnumAppSide.Client);
        api.SetupGet(value => value.Event).Returns(events.Object);
        api.SetupGet(value => value.Render).Returns(render);
        api.SetupGet(value => value.Shader).Returns(programs.Api);
        api.SetupGet(value => value.World.Player).Returns(CreatePlayer());
        api.SetupGet(value => value.World.SeaLevel).Returns(0);
        programs.Initialize(api.Object);
        using var frameCamera = TestFrameCamera.CreateIdentity(2, 2);
        GpuShaderPrograms.Get<AtmosphereSkyShaderProgram>(api.Object, "pbr_sky")!.FrameInputs = frameCamera;
        using var gbuffer = new GBufferManager(api.Object);
        Assert.True(gbuffer.EnsureBuffers(2, 2));
        using var sky = new AtmosphereSkyRenderer(api.Object, gbuffer);
        try
        {
            long startup = StateCache.Current.DrawSubmissions;
            sky.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.Equal(startup, StateCache.Current.DrawSubmissions);
            Assert.False(AtmosphereSkyDrawHook.Prefix());
        atmosphere.Publish(new(Vector3.UnitY, Vector3.One, Vector3.One, Vector3.Zero, Vector3.Zero,
            ImmutableArray.Create(.5f, .25f, .125f, 1f)) { Width = 1, Height = 1 });
            Assert.Equal(.2, sky.RenderOrder);
            events.Verify(value => value.RegisterRenderer(sky, EnumRenderStage.Opaque, "vge_sky"), Times.Once);
            for (int cycle = 0; cycle < 4; cycle++)
            {
                if (cycle == 1) ScreenResourceManager.HandleScreenResize();
                if (cycle == 2) events.Raise(value => value.ReloadShader += null!);
                if (cycle == 3) events.Raise(value => value.LeaveWorld += null!);
                Assert.False(AtmosphereSkyDrawHook.Prefix());
                Assert.True(gbuffer.EnsureBuffers(2, 2));
                engine.BindWithViewport();
                GL.ClearBuffer(ClearBuffer.Color, 0, new float[4]);
                GL.ClearBuffer(ClearBuffer.Color, 1, new float[4]);
                // Seed every attached metadata buffer so omitted writes cannot pass as coincidental zeros.
                DrawBuffersEnum[] routing = Enumerable.Range(0, 8)
                    .Select(i => i == 2 && !ssao ? DrawBuffersEnum.None : DrawBuffersEnum.ColorAttachment0 + i).ToArray();
                GL.DrawBuffers(8, routing);
                for (int attachment = 2; attachment < 8; attachment++)
                {
                    if (attachment == 2 && !ssao) continue;
                    if (attachment == 6) GL.ClearBuffer(ClearBuffer.Color, attachment, new uint[] { 6, 6, 6, 6 });
                    else GL.ClearBuffer(ClearBuffer.Color, attachment, Enumerable.Repeat((float)attachment, 4).ToArray());
                }
                ShaderProgramBase.CurrentShaderProgram = null;
                StateCache.Current.UseProgram(0);
                StateCache.Current.BindTexture(TextureTarget.Texture2D, 0, liquidDepth.TextureId);
                long before = StateCache.Current.DrawSubmissions;
                using (var hostile = new HostileFullscreenState())
                {
                    sky.OnRenderFrame(0, EnumRenderStage.Opaque);
                    hostile.AssertRestored();
                }
                Assert.True(before + 1 == StateCache.Current.DrawSubmissions, string.Join(Environment.NewLine, assets.Logs));
                Assert.Null(ShaderProgramBase.CurrentShaderProgram);
                Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
                int activeUnit = StateCache.Current.GetActiveTextureUnit();
                StateCache.Current.ActiveTexture(0);
                Assert.Equal(liquidDepth.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
                StateCache.Current.ActiveTexture(activeUnit);
                Assert.Equal(engine.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
                for (int attachment = 0; attachment < 8; attachment++)
                    Assert.Equal((int)routing[attachment], GL.GetInteger(GetPName.DrawBuffer0 + attachment));
                Assert.True(engine[0].ReadPixels()[0] > 0, string.Join(Environment.NewLine, assets.Logs));
                Assert.Equal(new float[] { 0, 0, 0, 1 }, engine[1].ReadPixels()[..4]);
                for (int attachment = 2; attachment < 8; attachment++)
                {
                    if (attachment == 2 && !ssao) continue;
                    // Metadata layers share one array allocation; read their framebuffer attachment instead of a 2D alias.
                    using var read = StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, engine.FboId);
                    int priorReadBuffer = GL.GetInteger(GetPName.ReadBuffer);
                    try
                    {
                        GL.ReadBuffer(ReadBufferMode.ColorAttachment0 + attachment);
                        if (attachment == 6)
                        {
                            uint[] pixels = new uint[16];
                            GL.ReadPixels(0, 0, 2, 2, PixelFormat.RgbaInteger, PixelType.UnsignedInt, pixels);
                            Assert.All(pixels, value => Assert.Equal(6u, value));
                        }
                        else
                        {
                            float[] pixels = new float[16];
                            GL.ReadPixels(0, 0, 2, 2, PixelFormat.Rgba, PixelType.Float, pixels);
                            Assert.All(pixels, value => Assert.Equal((float)attachment, value));
                        }
                    }
                    finally { GL.ReadBuffer((ReadBufferMode)priorReadBuffer); }
                }
            }
            Mock.Get(render).Verify(value => value.DeleteMesh(It.IsAny<MeshRef>()), Times.Never);
            Mock.Get(render).Verify(value => value.UploadMesh(It.IsAny<MeshData>()), Times.Never);
            frames[(int)EnumFrameBuffer.LiquidDepth] = null!;
            long missing = StateCache.Current.DrawSubmissions;
            sky.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.Equal(missing, StateCache.Current.DrawSubmissions);
            Assert.False(AtmosphereSkyDrawHook.Prefix());
            // A resource exception latches owned drawing until reload without reviving vanilla.
            frames[(int)EnumFrameBuffer.LiquidDepth] = new FrameBufferRef { DepthTextureId = liquidDepth.TextureId, Width = 2, Height = 2 };
            events.Raise(value => value.ReloadShader += null!);
            // Camera inversion belongs to the shared frame owner; fail the sky-owned effect capture instead.
            Mock.Get(render).SetupGet(value => value.ShaderUniforms).Throws(new InvalidOperationException("Fixture sky capture failure."));
            sky.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.Equal(missing, StateCache.Current.DrawSubmissions);
            Assert.False(AtmosphereSkyDrawHook.Prefix());
            Mock.Get(render).SetupGet(value => value.ShaderUniforms).Returns(uniforms);
            events.Raise(value => value.ReloadShader += null!);
            sky.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.Equal(missing + 1, StateCache.Current.DrawSubmissions);
            Assert.False(AtmosphereSkyDrawHook.Prefix());
            sky.Dispose();
            events.Verify(value => value.UnregisterRenderer(sky, EnumRenderStage.Opaque), Times.Once);
            Assert.False(AtmosphereSkyRenderer.IsEnabled);
            Mock.Get(render).Verify(value => value.DeleteMesh(It.IsAny<MeshRef>()), Times.Never);
            Mock.Get(render).Verify(value => value.UploadMesh(It.IsAny<MeshData>()), Times.Never);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            ShaderProgramBase.CurrentShaderProgram = null;
            StateCache.Current.UseProgram(0);
        }
    }
    #endregion

    #region Private
    /// <summary>Provides the installed client player's entity without creating client world subsystems.</summary>
    private static ClientPlayer CreatePlayer()
    {
        var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var data = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        typeof(ClientWorldPlayerData).GetField("entityplayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(data, new EntityPlayer());
        typeof(ClientPlayer).GetField("worlddata", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(player, data);
        return player;
    }
    #endregion
}
