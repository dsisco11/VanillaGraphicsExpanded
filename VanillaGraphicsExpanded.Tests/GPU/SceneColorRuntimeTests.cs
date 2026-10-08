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
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OwnedSceneRetainsHdrAcrossFailuresAndUnclassifiedRegistrations(int ssaoQuality)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        using var programs = new RuntimeLightingPrograms();
        using var drawing = new ShaderTestFramework();
        using var atmosphere = new AtmosphereModSystem();
        using var material = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        using var glow = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba8);
        using var depthStorage = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent32f);
        using var depth = GpuFramebufferAttachment.FromTexture(depthStorage);
        using var liquidDepth = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [1f, 1f, 1f, 1f]);
        using var normals = drawing.CreateTexture(2,2,PixelInternalFormat.Rgba16f,Enumerable.Repeat(new[]{0f,0f,1f,0f},4).SelectMany(value=>value).ToArray());
        using var positions = drawing.CreateTexture(2,2,PixelInternalFormat.Rgba16f,Enumerable.Repeat(new[]{1f,0f,-1f,0f},4).SelectMany(value=>value).ToArray());
        using var revealage = drawing.CreateTexture(2,2,PixelInternalFormat.R16f,[1f,1f,1f,1f]);
        // Installed SSAO allocates unsized RGB; bilateral outputs use RGBA8.
        using var ao = DynamicTexture2D.Create(2,2,PixelInternalFormat.Rgb);
        GL.GetTextureLevelParameter(ao.TextureId, 0, GetTextureParameter.TextureInternalFormat, out int nativeAoFormat);
        output.WriteLine($"Installed-style SSAO internal format: {(PixelInternalFormat)nativeAoFormat}");
        using var aoHorizontal = DynamicTexture2D.Create(2,2,PixelInternalFormat.Rgba8);
        using var aoVertical = DynamicTexture2D.Create(2,2,PixelInternalFormat.Rgba8);
        using var normalAttachment = GpuFramebufferAttachment.FromTexture(normals);
        using var positionAttachment = GpuFramebufferAttachment.FromTexture(positions);
        using var engine = GpuFramebuffer.Create([material, glow, normalAttachment, positionAttachment], depth);
        var primary = new FrameBufferRef { FboId = engine.FboId, Width = 2, Height = 2, DepthTextureId = depth.TextureId,
            ColorTextureIds = [material.TextureId, glow.TextureId, normals.TextureId, positions.TextureId] };
        using var postStorage = new GpuFramebufferAttachment(2, 2, PixelInternalFormat.Rgba16f);
        var frames = Enumerable.Repeat<FrameBufferRef>(null!, 25).ToList();
        frames[(int)EnumFrameBuffer.Primary] = primary;
        frames[(int)EnumFrameBuffer.Transparent] = new() { Width=2,Height=2,ColorTextureIds=[revealage.TextureId,revealage.TextureId] };
        frames[(int)EnumFrameBuffer.SSAO] = new() { Width=2,Height=2,ColorTextureIds=[ao.TextureId] };
        frames[(int)EnumFrameBuffer.SSAOBlurHorizontal] = new() { Width=2,Height=2,ColorTextureIds=[aoHorizontal.TextureId] };
        frames[(int)EnumFrameBuffer.SSAOBlurVertical] = new() { Width=2,Height=2,ColorTextureIds=[aoVertical.TextureId] };
        frames[(int)EnumFrameBuffer.LiquidDepth] = new FrameBufferRef { DepthTextureId = liquidDepth.TextureId, Width = 2, Height = 2 };
        foreach (var kind in new[] { EnumFrameBuffer.Luma, EnumFrameBuffer.FindBright,
            EnumFrameBuffer.BlurHorizontalMedRes, EnumFrameBuffer.BlurVerticalMedRes,
            EnumFrameBuffer.BlurHorizontalLowRes, EnumFrameBuffer.BlurVerticalLowRes, EnumFrameBuffer.GodRays })
            frames[(int)kind] = new FrameBufferRef { Width = 2, Height = 2, ColorTextureIds = [postStorage.TextureId] };
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
        using var gbuffer = new GBufferManager(api.Object);
        Assert.True(gbuffer.EnsureBuffers(2, 2));
        using var sky = new AtmosphereSkyRenderer(api.Object, gbuffer);

        using var directTargets = new DirectLightingBufferManager(api.Object);
        using var direct = new DirectLightingRenderer(api.Object, gbuffer, directTargets);
        using var composite = new PBRCompositeRenderer(api.Object, gbuffer, directTargets, null, () => null, () => false);
        using var particles = new SceneColorParticleCapture(api.Object, gbuffer);
        using var cube = new InstalledShaderFixture("particlescube");
        using var surface = new InstalledShaderFixture("standard");
        using var final = new InstalledShaderFixture("final", effects: true);
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
        var harmony = new Harmony("VGE.Tests.SceneColorRuntime");
        try
        {
            ConfigModSystem.Config.CameraExposure = new() { Enabled = false };
            camera.OnRenderFrame(0, EnumRenderStage.Before);
            // Supply a controlled installed registry; unknown registrations are tested below.
            registry.SetValue(null, new ShaderProgram[] { cube.Engine, final.Engine, oit.Engine });
            ShaderPrograms.Particlescube = (ShaderProgramParticlescube)cube.Engine;
            ShaderPrograms.Final = (ShaderProgramFinal)final.Engine;
            PbrDrawRouteHook.Api = api.Object;
            harmony.CreateClassProcessor(typeof(PbrDrawRouteHook)).Patch();
            atmosphere.Publish(new(Vector3.UnitY, Vector3.One, Vector3.One, Vector3.Zero, Vector3.Zero,
                ImmutableArray.Create(8f, 4f, 2f, 1f)) { Width = 1, Height = 1 });
            particles.OnRenderFrame(0, EnumRenderStage.Before);
            ShaderPrograms.Final = null;
            var failure = Assert.Throws<InvalidOperationException>(() => scene.OnRenderFrame(0, EnumRenderStage.Before));
            Assert.Contains("final display", failure.Message);
            Assert.True(SceneColorPipeline.HasSceneInput);
            cube.Engine.Use();
            AssertMode(cube.Engine, 1);
            Assert.Throws<InvalidOperationException>(() => SceneColorParticleCapture.BeginDraw(1));
            cube.Engine.Stop();
            ShaderPrograms.Final = (ShaderProgramFinal)final.Engine;
            scene.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.True(SceneColorPipeline.HasSceneInput, string.Join(Environment.NewLine, assets.Logs));
            engine.BindWithViewport();
            GL.ClearBuffer(ClearBuffer.Color, 0, new float[4]);
            GL.DepthMask(true);
            GL.ClearBuffer(ClearBuffer.Depth, 0, new[] { 1f });
            sky.OnRenderFrame(0, EnumRenderStage.Opaque);
            Assert.True(engine[0].ReadPixels()[0] > 1f, "Owned sky must retain HDR radiance in primary.");

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
            post.Render(identity, new(true, true, ssaoQuality > 0, true, ssaoQuality, new float[192]));
            if (ssaoQuality > 0) Assert.All(aoVertical.ReadPixels(), value => Assert.InRange(value, .999f, 1.001f));
            int publishedExposure = CameraExposureRenderer.DisplayExposure().Texture!.TextureId;
            Assert.True(GL.IsTexture(publishedExposure));
            final.Engine.Use();
            PostprocessPipeline.BindBloom((ShaderProgramFinal)final.Engine, postStorage.TextureId);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.GetInteger(GetPName.TextureBinding2D, out int ownedBloom);
            Assert.NotEqual(postStorage.TextureId, ownedBloom);
            Assert.True(GL.IsTexture(ownedBloom));
            PostprocessPipeline.BindGodRays((ShaderProgramFinal)final.Engine, postStorage.TextureId);
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.GetInteger(GetPName.TextureBinding2D, out int ownedRays);
            Assert.NotEqual(postStorage.TextureId, ownedRays);
            Assert.True(GL.IsTexture(ownedRays));
            final.Engine.Stop();
            StateCache.Current.InvalidateAll();
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
            using (var textureScope = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 15, postStorage.TextureId))
            using (var samplerScope = StateCache.Current.BindSamplerScope(15, GpuSamplers.LinearClamp.SamplerId))
            {
                SceneColorProgramBindings.UsePostprocess(final.Engine);
                GL.GetUniform(final.Engine.ProgramId, GL.GetUniformLocation(final.Engine.ProgramId, "vge_cameraExposureEnabled"), out int enabled);
                Assert.Equal(1, enabled);
                final.Engine.Stop();
                SceneColorPostprocessBindingHook.Finalizer(postprocess);
                GL.ActiveTexture(TextureUnit.Texture15);
                GL.GetInteger(GetPName.TextureBinding2D, out int restoredTexture);
                GL.GetInteger(GetPName.SamplerBinding, out int restoredSampler);
                Assert.Equal(postStorage.TextureId, restoredTexture);
                Assert.Equal(GpuSamplers.LinearClamp.SamplerId, restoredSampler);
                StateCache.Current.InvalidateAll();
                final.Engine.Use();
                GL.GetUniform(final.Engine.ProgramId, GL.GetUniformLocation(final.Engine.ProgramId, "vge_cameraExposureEnabled"), out enabled);
                Assert.Equal(0, enabled);
                final.Engine.Stop();
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

            final.Engine.Use();
            AssertMode(final.Engine, 0);
            final.Engine.Stop();
            SceneColorProgramBindings.UsePostprocess(final.Engine);
            AssertMode(final.Engine, 1);
            using var zeroEffects = drawing.CreateTexture(1,1,PixelInternalFormat.Rgba16f,[0f,0f,0f,0f]);
            ((ShaderProgramFinal)final.Engine).BloomParts2D = zeroEffects.TextureId;
            ((ShaderProgramFinal)final.Engine).GodrayParts2D = zeroEffects.TextureId;
            final.Engine.Uniform("primaryScene", 0);
            final.Engine.Uniform("gammaLevel", 1f);
            final.Engine.Uniform("brightnessLevel", 1f);
            using (var output = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f))
            using (var source = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, material.TextureId))
            {
                drawing.RenderQuadTo(final.Engine.ProgramId, output);
                float[] display = output[0].ReadPixels();
                float peak = Math.Max(handedOff[0], Math.Max(handedOff[1], handedOff[2]));
                for (int channel = 0; channel < 3; channel++)
                {
                    float value = handedOff[channel] / (1 + peak);
                    float expected = value <= .0031308f ? 12.92f * value : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;
                    Assert.InRange(display[channel], expected - 1f / 255, expected + 1f / 255);
                }
            }
            final.Engine.Stop();
            SceneColorPostprocessBindingHook.Finalizer(AccessTools.Method(typeof(ClientPlatformWindows),
                nameof(ClientPlatformWindows.RenderPostprocessingEffects)), new InvalidOperationException("Intermediate failed"));
            Assert.True(SceneColorPipeline.HasSceneInput);
            SceneColorPostprocessBindingHook.Finalizer(AccessTools.Method(typeof(ClientPlatformWindows),
                nameof(ClientPlatformWindows.RenderFinalComposition)));
            Assert.False(SceneColorPipeline.HasSceneInput);
            SceneColorProgramBindings.UsePostprocess(final.Engine);
            AssertMode(final.Engine, 0);
            final.Engine.Stop();

            // Unrelated shader registrations cannot change the VGE scene convention.
            registry.SetValue(null, new ShaderProgram[] { cube.Engine, new() { PassName = "unclassified", ProgramId = 1 } });
            scene.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.True(SceneColorPipeline.HasSceneInput);
            cube.Engine.Use();
            AssertMode(cube.Engine, 1);
            cube.Engine.Stop();
            registry.SetValue(null, new ShaderProgram[] { cube.Engine, final.Engine });
            scene.OnRenderFrame(0, EnumRenderStage.Before);
            Assert.True(SceneColorPipeline.HasSceneInput, string.Join(Environment.NewLine, assets.Logs));
            ConfigModSystem.Config.CameraExposure = new() { Enabled = true };
            camera.OnRenderFrame(0, EnumRenderStage.Before);
            CameraExposureRenderer.MeterScene();
            int beforeResize = CameraExposureRenderer.DisplayExposure().Texture!.TextureId;
            ScreenResourceManager.HandleScreenResize();
            Assert.False(GL.IsTexture(beforeResize));
            Assert.False(GL.IsTexture(ownedBloom));
            Assert.False(GL.IsTexture(ownedRays));
            Assert.True(GL.IsTexture(material.TextureId));
            Assert.True(GL.IsTexture(postStorage.TextureId));
            Assert.Throws<InvalidOperationException>(() => CameraExposureRenderer.DisplayExposure());
            camera.OnRenderFrame(0, EnumRenderStage.Before);
            CameraExposureRenderer.MeterScene();
            // Recreate borrowed luma resources after resize and verify collection retirement never owns engine storage.
            post.OnRenderFrame(0, EnumRenderStage.Before);
            post.Render(identity, new(false, false, false, true, 0, []));
            int beforeReload = CameraExposureRenderer.DisplayExposure().Texture!.TextureId;
            events.Raise(value => value.ReloadShader += null!);
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
            CameraExposureDisplayBindings.EndBinding();
            ConfigModSystem.Config.CameraExposure = previousExposure;
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
