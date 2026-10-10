using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes mandatory HDR preparation and installed binding transitions with real owned producer resources.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorRuntimeTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Prepared sky radiance survives primary while reused programs reset on offscreen and final boundaries.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void OwnedSceneRetainsHdrAcrossFailuresAndUnclassifiedRegistrations(int ssaoQuality, bool lumOn)
    {
        EnsureContextValid();
        bool? previousGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
        PbrShaderLightingMode.GenerationLumOnEnabled = lumOn;
        bool previousLumOn = ConfigModSystem.Config.LumOn.Enabled;
        ConfigModSystem.Config.LumOn.Enabled = lumOn;
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var programs = new RuntimeLightingPrograms();
        using var drawing = new ShaderTestFramework();
        using var atmosphere = new AtmosphereModSystem();
        using var materialTexture = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba16f);
        using var material = GpuFramebufferAttachment.FromTexture(materialTexture);
        using var glowTexture = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba8);
        using var glow = GpuFramebufferAttachment.FromTexture(glowTexture);
        using var depthStorage = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent32f);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var liquidDepth = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [1f, 1f, 1f, 1f]);
        using var normals = drawing.CreateTexture(2,2,PixelInternalFormat.Rgba16f,Enumerable.Repeat(new[]{0f,0f,1f,0f},4).SelectMany(value=>value).ToArray());
        using var positions = drawing.CreateTexture(2,2,PixelInternalFormat.Rgba16f,Enumerable.Repeat(new[]{1f,0f,-1f,0f},4).SelectMany(value=>value).ToArray());
        using var revealage = drawing.CreateTexture(2,2,PixelInternalFormat.R16f,[1f,1f,1f,1f]);
        using var normalAttachment = GpuFramebufferAttachment.FromTexture(normals);
        using var positionAttachment = GpuFramebufferAttachment.FromTexture(positions);
        using var engine = GpuFramebuffer.Create([material, glow, normalAttachment, positionAttachment], depth);
        var primary = new FrameBufferRef { FboId = engine.FboId, Width = 2, Height = 2, DepthTextureId = depth.TextureId,
            ColorTextureIds = [material.TextureId, glow.TextureId, normals.TextureId, positions.TextureId] };
        using var postStorageTexture = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba16f);
        using var postStorage = GpuFramebufferAttachment.FromTexture(postStorageTexture);
        var frames = Enumerable.Repeat<FrameBufferRef>(null!, 25).ToList();
        frames[(int)EnumFrameBuffer.Primary] = primary;
        frames[(int)EnumFrameBuffer.Transparent] = new() { Width=2,Height=2,ColorTextureIds=[revealage.TextureId,revealage.TextureId] };
        frames[(int)EnumFrameBuffer.LiquidDepth] = new FrameBufferRef { DepthTextureId = liquidDepth.TextureId, Width = 2, Height = 2 };
        // Legacy postprocess slots remain absent: the owner must allocate every intermediate itself.
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        var uniforms = new DefaultShaderUniforms { ZNear = .1f, ZFar = 100, WaterMurkColor = new Vec4f() };
        typeof(DefaultShaderUniforms).GetField("SkyDaylight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(uniforms, 1f);
        var render = RuntimeEngineServices.Render(2, frames, () => identity, () => identity, () => drawing.RenderGeometry(), uniforms);
        Mock.Get(render).SetupGet(value => value.CurrentModelviewMatrix).Returns(identity);
        Mock.Get(render).SetupGet(value => value.CameraType).Returns(EnumCameraMode.FirstPerson);
        Mock.Get(render).SetupGet(value => value.WireframeDebugRender).Returns(new WireframeModes());
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
        using var frameCamera = new VgeFrameRenderer(api.Object);
        using var lights = new VgeLightsRenderer(api.Object);
        using var gbuffer = new GBufferManager(api.Object);
        Assert.True(gbuffer.EnsureBuffers(2, 2));
        using var sky = new AtmosphereSkyRenderer(api.Object, gbuffer);

        using var directTargets = new DirectLightingBufferManager(api.Object);
        using var direct = new DirectLightingRenderer(api.Object, gbuffer, directTargets);
        using var composite = new PBRCompositeRenderer(api.Object, gbuffer, directTargets, null, () => null, () => false);
        using var particles = new SceneColorParticleCapture(api.Object, gbuffer);
        using var cube = new InstalledShaderFixture("particlescube");
        using var surface = new InstalledShaderFixture("standard");
        using var oit = new InstalledShaderFixture("particlesquad2d");
        using var scene = new SceneColorPipeline(api.Object, composite, direct, directTargets, sky, particles);
        var registry = AccessTools.Field(typeof(ShaderRegistry), "shaderPrograms");
        var previousPrograms = registry.GetValue(null);
        var previousCube = ShaderPrograms.Particlescube;
        var previousFinal = ShaderPrograms.Final;
        var previousApi = PbrDrawRouteHook.Api;
        var previousExposure = ConfigModSystem.Config.CameraExposure;
        using var camera = new CameraExposureRenderer(api.Object);
        using var post = new PostprocessPipeline(api.Object);
        using var shaftOcclusion = new LightShaftOcclusionRenderer(api.Object);
        using var hierarchy = new DepthHierarchyRenderer(api.Object);
        using var ambientOcclusion = new AmbientOcclusionRenderer(api.Object, gbuffer);
        api.SetupGet(value => value.Settings.Int["ssaoQuality"]).Returns(ssaoQuality);
        api.SetupGet(value => value.Settings.Int["godRays"]).Returns(2);
        Vintagestory.Client.ScreenManager.Platform.DoPostProcessingEffects = true;
        var harmony = new Harmony("VGE.Tests.SceneColorRuntime");
        try
        {
            ConfigModSystem.Config.CameraExposure = new() { Enabled = false };
            camera.OnRenderFrame(0, EnumRenderStage.Before);
            // Supply a controlled installed registry; unknown registrations are tested below.
            registry.SetValue(null, new ShaderProgram[] { cube.Engine, oit.Engine });
            ShaderPrograms.Particlescube = (ShaderProgramParticlescube)cube.Engine;
            ShaderPrograms.Final = null;
            PbrDrawRouteHook.Api = api.Object;
            harmony.CreateClassProcessor(typeof(PbrDrawRouteHook)).Patch();
            atmosphere.Publish(new(Vector3.UnitY, Vector3.One, Vector3.One, Vector3.Zero, Vector3.Zero,
                ImmutableArray.Create(8f, 4f, 2f, 1f)) { Width = 1, Height = 1 });
            particles.OnRenderFrame(0, EnumRenderStage.Before);
            scene.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.True(SceneColorPipeline.HasSceneInput, string.Join(Environment.NewLine, assets.Logs));
            frameCamera.OnRenderFrame(0, EnumRenderStage.Before);
            lights.OnRenderFrame(0, EnumRenderStage.Before);
            frameCamera.OnRenderFrame(0, EnumRenderStage.Opaque);
            lights.OnRenderFrame(0, EnumRenderStage.Opaque);
            engine.BindWithViewport();
            GL.ClearBuffer(ClearBuffer.Color, 0, new float[4]);
            GL.DepthMask(true);
            GL.ClearBuffer(ClearBuffer.Depth, 0, new[] { 1f });
            sky.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.True(engine[0].ReadPixels()[0] > 1f, "Owned sky must retain HDR radiance in primary.");

            shaftOcclusion.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.Null(LightShaftOcclusionRenderer.Texture);
            Assert.True(shaftOcclusion.RenderOrder < composite.RenderOrder);
            shaftOcclusion.OnRenderFrame(0, EnumRenderStage.Opaque);
            int earlyOcclusion = LightShaftOcclusionRenderer.Texture!.TextureId;
            hierarchy.OnRenderFrame(0, EnumRenderStage.Before);
            ambientOcclusion.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.Null(AmbientOcclusionRenderer.Texture);
            hierarchy.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.True(ambientOcclusion.RenderOrder < composite.RenderOrder);
            ambientOcclusion.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.Equal(ssaoQuality > 0, AmbientOcclusionRenderer.Texture is not null);
            ambientOcclusion.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.Null(AmbientOcclusionRenderer.Texture);
            ambientOcclusion.OnRenderFrame(0, EnumRenderStage.Opaque);
            api.SetupGet(value => value.Settings.Int["ssaoQuality"]).Returns(0);
            ambientOcclusion.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.Null(AmbientOcclusionRenderer.Texture);
            api.SetupGet(value => value.Settings.Int["ssaoQuality"]).Returns(ssaoQuality);

            Assert.Equal(2, LightShaftOcclusionRenderer.Texture.Width);
            Assert.Equal(2, LightShaftOcclusionRenderer.Texture.Height);
            shaftOcclusion.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.Null(LightShaftOcclusionRenderer.Texture);
            shaftOcclusion.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.Equal(earlyOcclusion, LightShaftOcclusionRenderer.Texture!.TextureId);
            float[] skyPixels = engine[0].ReadPixels();
            Assert.True(direct.RenderLighting());
            composite.OnRenderFrame(0, EnumRenderStage.Opaque);
            float[] handedOff = engine[0].ReadPixels();
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(handedOff[channel], skyPixels[channel] - .02f, skyPixels[channel] + .02f);

            // Surface programs have only the existing route selector, including nested UI reuse.
            Assert.False(surface.Engine.HasUniform("vge_sceneLinear"));
            foreach (var (stage, target, expected) in new[]
            {
                (EnumRenderStage.Opaque, primary, 1),
                (EnumRenderStage.AfterOIT, primary, 2),
                (EnumRenderStage.Opaque, new FrameBufferRef { FboId = -1 }, 0),
                (EnumRenderStage.Opaque, primary, 1)
            })
            {
                Mock.Get(render).SetupGet(value => value.CurrentRenderStage).Returns(stage);
                Mock.Get(render).SetupGet(value => value.CurrentFrameBuffer).Returns(target);
                surface.Engine.Use();
                GL.GetUniform(surface.Engine.ProgramId, GL.GetUniformLocation(surface.Engine.ProgramId, "vge_pbrRoute"), out int route);
                Assert.Equal(expected, route);
                surface.Engine.Stop();
            }

            // Other engine programs still need their own scene/offscreen color binding.
            cube.Engine.Use();
            AssertMode(cube.Engine, 1);
            cube.Engine.Stop();
            Mock.Get(render).SetupGet(value => value.CurrentFrameBuffer).Returns(new FrameBufferRef { FboId = -1 });
            cube.Engine.Use();
            AssertMode(cube.Engine, 0);
            cube.Engine.Stop();
            Mock.Get(render).SetupGet(value => value.CurrentFrameBuffer).Returns(primary);
            cube.Engine.Use();
            AssertMode(cube.Engine, 1);
            cube.Engine.Stop();

            // Meter the completed scene and lend the actual owner's history to the installed final shader.
            ConfigModSystem.Config.CameraExposure = new() { Enabled = true, CenterWeighted = false };
            camera.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.Throws<InvalidOperationException>(() => CameraExposureRenderer.DisplayExposure());
            var postprocess = AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RenderPostprocessingEffects));
            post.OnRenderFrame(0, EnumRenderStage.Before);
            post.Render(new(true, true, ssaoQuality > 0, true, ssaoQuality));
            Assert.Equal(earlyOcclusion, LightShaftOcclusionRenderer.Texture!.TextureId);
            int publishedExposure = CameraExposureRenderer.DisplayExposure().Texture!.TextureId;
            Assert.True(GL.IsTexture(publishedExposure));
            int ownedBloom = ((GpuTexture)AccessTools.Field(typeof(PostprocessPipeline), "bloomTexture").GetValue(post)!).TextureId;
            int ownedRays = ((GpuTexture)AccessTools.Field(typeof(PostprocessPipeline), "rayTexture").GetValue(post)!).TextureId;
            Assert.NotEqual(postStorage.TextureId, ownedBloom);
            Assert.NotEqual(postStorage.TextureId, ownedRays);
            Assert.True(GL.IsTexture(ownedBloom)); Assert.True(GL.IsTexture(ownedRays));
            using (var borrowed = GpuFramebufferAttachment.FromTextureId(publishedExposure))
            using (var readback = GpuFramebuffer.Create([borrowed]))
            {
                float luma = handedOff[0] * .2126f + handedOff[1] * .7152f + handedOff[2] * .0722f;
                float expectedEv = Math.Clamp(MathF.Log2(.18f / luma), -4, 4);
                readback.BindWithViewport();
                float[] evPixel = new float[1];
                GL.ReadPixels(0, 0, 1, 1, PixelFormat.Red, PixelType.Float, evPixel);
                Assert.InRange(evPixel[0], expectedEv - .002f, expectedEv + .002f);
            }
            // Device queries surround both warmed passes, with no per-frame image readback.
            var milliseconds = new List<double>();
            for (int sample = 0; sample < 8; sample++)
            {
                camera.OnRenderFrame(1f / 60, EnumRenderStage.Before);
                using var timer = GpuTimerQuery.Create();
                timer.Begin(); CameraExposureRenderer.MeterScene(); timer.End();
                milliseconds.Add(timer.GetResultNanoseconds() / 1e6);
            }
            output.WriteLine($"Synthetic camera metering (2x2 uniform scene, fixed 64x36 grid), 8 warmed GPU samples: min={milliseconds.Min():F4} ms, median={milliseconds.Order().ElementAt(4):F4} ms, max={milliseconds.Max():F4} ms. This is not in-game frame cost.");
            int beforeRetire = CameraExposureRenderer.DisplayExposure().Texture!.TextureId;
            ConfigModSystem.Config.CameraExposure = new() { Enabled = false };
            camera.OnRenderFrame(0, EnumRenderStage.Before);
            CameraExposureRenderer.MeterScene();
            Assert.Null(CameraExposureRenderer.DisplayExposure().Texture);
            Assert.Equal(0, CameraExposureRenderer.DisplayExposure().ManualEV);
            Assert.False(GL.IsTexture(beforeRetire));

            // The owned final reads owned luma and writes primary once, without the engine final program.
            post.OnRenderFrame(0, EnumRenderStage.Before);
            post.Render(new(false, false, ssaoQuality > 0, false, ssaoQuality));
            var displayParameters = new FinalDisplayParameters(new(1,1,1,0),Vector4.Zero,Vector4.Zero);
            engine.BindWithViewport();
            GL.GetInteger(GetPName.DrawBuffer0, out int beforeDrawRoute);
            using (var textureScope = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 15, postStorage.TextureId))
            using (var samplerScope = StateCache.Current.BindSamplerScope(15, GpuSamplers.LinearClamp.SamplerId))
            {
                post.RenderFinal(displayParameters);
                GL.ActiveTexture(TextureUnit.Texture15);
                GL.GetInteger(GetPName.TextureBinding2D, out int restoredTexture);
                GL.GetInteger(GetPName.SamplerBinding, out int restoredSampler);
                Assert.Equal(postStorage.TextureId, restoredTexture);
                Assert.Equal(GpuSamplers.LinearClamp.SamplerId, restoredSampler);
                StateCache.Current.InvalidateAll();
            }
            GL.GetInteger(GetPName.DrawFramebufferBinding, out int restoredFramebuffer);
            GL.GetInteger(GetPName.DrawBuffer0, out int restoredDrawRoute);
            Assert.Equal(engine.FboId, restoredFramebuffer);
            Assert.Equal(beforeDrawRoute, restoredDrawRoute);
            float[] display = engine[0].ReadPixels();
            float peak = Math.Max(handedOff[0], Math.Max(handedOff[1], handedOff[2]));
            for (int channel = 0; channel < 3; channel++)
            {
                float value = handedOff[channel] / (1 + peak);
                float expected = value <= .0031308f ? 12.92f * value : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;
                Assert.InRange(display[channel], expected - 1f / 255, expected + 1f / 255);
            }
            Assert.Equal(1, display[3]);
            Assert.Throws<InvalidOperationException>(() => post.RenderFinal(displayParameters));
            Assert.Throws<InvalidOperationException>(() => PostprocessPipeline.ReplaceFinalPass(
                (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows))));
            Assert.False(SceneColorPipeline.HasSceneInput);

            // Unrelated shader registrations cannot change the VGE scene convention.
            registry.SetValue(null, new ShaderProgram[] { cube.Engine, new() { PassName = "unclassified", ProgramId = 1 } });
            scene.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.True(SceneColorPipeline.HasSceneInput);
            cube.Engine.Use();
            AssertMode(cube.Engine, 1);
            cube.Engine.Stop();
            registry.SetValue(null, new ShaderProgram[] { cube.Engine });
            scene.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.True(SceneColorPipeline.HasSceneInput, string.Join(Environment.NewLine, assets.Logs));
            ConfigModSystem.Config.CameraExposure = new() { Enabled = true };
            camera.OnRenderFrame(0, EnumRenderStage.Before);
            CameraExposureRenderer.MeterScene();
            int beforeResize = CameraExposureRenderer.DisplayExposure().Texture!.TextureId;
            ScreenResourceManager.HandleScreenResize();
            Assert.False(GL.IsTexture(beforeResize));
            Assert.False(GL.IsTexture(earlyOcclusion));
            Assert.Null(LightShaftOcclusionRenderer.Texture);
            Assert.False(GL.IsTexture(ownedBloom));
            Assert.False(GL.IsTexture(ownedRays));
            Assert.True(GL.IsTexture(material.TextureId));
            Assert.True(GL.IsTexture(postStorage.TextureId));
            Assert.Throws<InvalidOperationException>(() => CameraExposureRenderer.DisplayExposure());
            camera.OnRenderFrame(0, EnumRenderStage.Before);
            CameraExposureRenderer.MeterScene();
            // Recreate borrowed luma resources after resize and verify collection retirement never owns engine storage.
            post.OnRenderFrame(0, EnumRenderStage.Before);
            post.Render(new(false, false, false, true, 0));
            int beforeReload = CameraExposureRenderer.DisplayExposure().Texture!.TextureId;
            shaftOcclusion.OnRenderFrame(0, EnumRenderStage.Opaque);
            int beforeNativeDisable = LightShaftOcclusionRenderer.Texture!.TextureId;
            api.SetupGet(value => value.Settings.Int["godRays"]).Returns(0);
            shaftOcclusion.OnRenderFrame(0, EnumRenderStage.Before);
            shaftOcclusion.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.Null(LightShaftOcclusionRenderer.Texture);
            Assert.False(GL.IsTexture(beforeNativeDisable));
            api.SetupGet(value => value.Settings.Int["godRays"]).Returns(2);
            shaftOcclusion.OnRenderFrame(0, EnumRenderStage.Opaque);
            int beforeShaftReload = LightShaftOcclusionRenderer.Texture!.TextureId;
            events.Raise(value => value.ReloadShader += null!);
            Assert.Null(LightShaftOcclusionRenderer.Texture);
            Assert.False(GL.IsTexture(beforeShaftReload));
            Assert.False(GL.IsTexture(beforeReload));
            Assert.True(GL.IsTexture(material.TextureId));
            Assert.True(GL.IsTexture(postStorage.TextureId));
            Assert.Throws<InvalidOperationException>(() => CameraExposureRenderer.DisplayExposure());
            Assert.True(SceneColorPipeline.HasSceneInput);
            cube.Engine.Use();
            AssertMode(cube.Engine, 1);
            cube.Engine.Stop();
            events.Raise(value => value.LeaveWorld += null!);
            Assert.False(SceneColorPipeline.HasSceneInput);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            ConfigModSystem.Config.CameraExposure = previousExposure;
            ConfigModSystem.Config.LumOn.Enabled = previousLumOn;
            PbrShaderLightingMode.GenerationLumOnEnabled = previousGeneration;
            harmony.UnpatchAll(harmony.Id);
            PbrDrawRouteHook.Api = previousApi;
            ShaderPrograms.Particlescube = previousCube;
            ShaderPrograms.Final = previousFinal;
            registry.SetValue(null, previousPrograms);
            ShaderProgramBase.CurrentShaderProgram = null;
            StateCache.Current.UseProgram(0);
        }
    }
    #endregion

    #region Private
    /// <summary>Reads the actual linked uniform instead of trusting managed routing metadata.</summary>
    private static void AssertMode(ShaderProgramBase program, int expected)
    {
        GL.GetUniform(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_sceneLinear"), out int actual);
        Assert.Equal(expected, actual);
    }

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
