using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Executes five real sky programs with their separate generated inputs against simultaneous color/glow attachments.</summary>
internal sealed class ProductionSkyDrawScene : IDisposable
{
    private const int Size = 32;
    private readonly List<IDisposable> owned = [];
    private readonly SkyDrawScene native;
    private readonly GpuProgram[] programs;
    private readonly GraphicsPipeline[] pipelines;
    private readonly ArrayGraphicsGeometry fullscreen, stars;
    private readonly DynamicTexture2D[] outputs;
    private readonly RenderPassDesc pass;
    private readonly SkyDrawTestPass[] passes;
    private readonly List<string> events = [];
    internal readonly HashSet<GraphicsCommandContext> Contexts = [];
    internal readonly List<string> Draws = [];
    internal readonly List<float[][]> PrefixImages = [];
    internal bool ReadPrefixes;

    #region Public API
    #region Setup
    /// <summary>Authors independent source, disk and atmospheric inputs without world-admission or catalog-generation work.</summary>
    internal ProductionSkyDrawScene(float variation)
    {
        native = Own(new SkyDrawScene());
        try
        {
            var library = Own(new ComponentShaderPrograms());
            var camera = Own(TestFrameCamera.CreateSolar(Size, Size));
            var storage = Own(new TestFrameLightingPublication());
            storage.Capture(.001f);
            var galaxy = library.Create<CelestialGalaxyProgram>();
            var star = library.Create<CelestialStarProgram>();
            var moon = library.Create<MoonProgram>();
            var sun = library.Create<SunProgram>();
            var sky = library.Create<AtmosphereSkyShaderProgram>();
            programs = [galaxy, star, moon, sun, sky];
            galaxy.Frame = star.Frame = camera;
            moon.Frame = sun.Frame = camera;
            sky.FrameInputs = camera;
            galaxy.FrameLightingInputs = star.FrameLightingInputs = storage.Buffer;
            moon.FrameLightingInputs = sun.FrameLightingInputs = sky.FrameLightingInputs = storage.Buffer;
            galaxy.DrawInputs.Capture(Matrix4x4.Identity, variation, -1);
            star.DrawInputs.Capture(Matrix4x4.Identity, 1, -1);
            var cube = Own(DynamicTextureCube.Create(1, PixelInternalFormat.Rgba32f));
            for (int face = 0; face < 6; face++)
            {
                using var binding = cube.BindScope(0);
                GL.TexSubImage2D(TextureTarget.TextureCubeMapPositiveX + face, 0, 0, 0, 1, 1,
                    PixelFormat.Rgba, PixelType.Float, new float[] { .02f, .01f, .005f, 1 });
            }
            var transmission = Own(DynamicTexture2D.CreateWithData(2, 1, PixelInternalFormat.Rgba32f,
                [.8f, .7f, .6f, 1f, .8f, .7f, .6f, 1f]));
            galaxy.GalaxyMap = star.GalaxyMap = cube;
            galaxy.ExternalTransmission = star.ExternalTransmission = transmission;
            moon.DrawInputs.Capture(new(Vector3.Normalize(new(-.002f, 0, -1)), Vector3.UnitX,
                Vector3.UnitY, Vector3.UnitZ, new(.05f * variation, .1f, .2f), .002f, -1));
            float[] lookup = Enumerable.Range(0, 8).SelectMany(_ => new[] { .003f, .002f * variation, .001f, 1f }).ToArray();
            var atmosphere = Own(new AtmosphereModSystem());
            atmosphere.Publish(new(-Vector3.UnitZ, new(.01f, .008f * variation, .006f), Vector3.Zero,
                Vector3.Zero, Vector3.Zero, ImmutableArray.Create(lookup))
            { Width = 4, Height = 2, HorizonElevation = -1 });
            var lighting = AtmosphereModSystem.Lighting!;
            sun.DrawInputs.Capture(lighting, .001f);
            var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
            var uniforms = new DefaultShaderUniforms { WaterMurkColor = new Vec4f(), PlayerPos = new Vec3f() };
            AccessTools.Field(typeof(DefaultShaderUniforms), "SkyDaylight").SetValue(uniforms, 1f);
            api.SetupGet(value => value.Render.ShaderUniforms).Returns(uniforms);
            api.SetupGet(value => value.Ambient.BlendedFogMin).Returns(.25f);
            api.SetupGet(value => value.World.Player).Returns(CreatePlayer());
            var depth = Own(DynamicTexture2D.CreateWithData(1, 1, PixelInternalFormat.R32f, [1f]));
            var frames = Enumerable.Repeat<FrameBufferRef>(null!, 25).ToList();
            frames[(int)EnumFrameBuffer.LiquidDepth] = new() { DepthTextureId = depth.TextureId };
            api.SetupGet(value => value.Render.FrameBuffers).Returns(frames);
            // This owner supplies the real owned-celestial compatibility flag; no callback admission is claimed.
            Own(new CelestialDrawPass(api.Object, null!));
            sky.Capture(api.Object, lighting, true);
            outputs = Enumerable.Range(0, 2).Select(_ => Own(DynamicTexture2D.Create(Size, Size, PixelInternalFormat.Rgba32f))).ToArray();
            var target = Own(DynamicGpuFramebuffer.CreateMRT(outputs)!);
            pass = new(target, [new(0), new(1)]);
            using var metadata = new RenderPassTargets(pass);
            fullscreen = Own(new ArrayGraphicsGeometry(CelestialDrawPass.FullscreenLayout, PrimitiveType.Triangles,
                new Dictionary<int, GpuVbo>(), 3));
            var stream = Own(RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource());
            stream.UploadData(new[] { new Vector4(Vector3.Normalize(new(.002f, .001f, -1)), .001f * variation), new Vector4(1, .5f, .25f, 0) });
            stars = Own(new ArrayGraphicsGeometry(CelestialStarGeometry.Layout, PrimitiveType.Triangles,
                new Dictionary<int, GpuVbo> { [0] = stream }));
            var lifetime = Own(new GraphicsPipelineLifetime());
            pipelines = new GraphicsPipeline[5];
            for (int index = 0; index < 5; index++)
            {
                var blend = index < 4 ? new ColorBlendDesc
                {
                    Enabled = true,
                    SourceRgb = BlendingFactorSrc.One,
                    DestinationRgb = BlendingFactorDest.One,
                    SourceAlpha = BlendingFactorSrc.Zero,
                    DestinationAlpha = BlendingFactorDest.One
                }
                    : new ColorBlendDesc
                    {
                        Enabled = true,
                        SourceRgb = BlendingFactorSrc.One,
                        DestinationRgb = BlendingFactorDest.OneMinusSrcAlpha,
                        SourceAlpha = BlendingFactorSrc.SrcAlpha,
                        DestinationAlpha = BlendingFactorDest.OneMinusSrcAlpha
                    };
                pipelines[index] = Own(new GraphicsPipeline(lifetime, new(programs[index].GraphicsIdentity!,
                    index == 1 ? CelestialStarGeometry.Layout : CelestialDrawPass.FullscreenLayout,
                    metadata.Signature, DynamicPipelineState.Viewport, blending: [blend, blend]), programs[index]));
            }
            passes = Enumerable.Range(0, 4).Select(index => new SkyDrawTestPass(index,
                index == 0 ? pipelines[..2] : [pipelines[index + 1]], commands => Draw(commands, index), events)).ToArray();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Restores identical hostile inherited graphics state and independent black MRT content before each candidate.</summary>
    internal void Initialize()
    {
        foreach (var image in outputs) image.UploadDataImmediate(new float[Size * Size * 4]);
        Draws.Clear();
        Contexts.Clear();
        PrefixImages.Clear();
        native.Initialize();
    }
    #endregion

    #region Submission and inspection
    /// <summary>Prepares the four complete optional-pass footprints before any graphics boundary is entered.</summary>
    internal SkyDraw[] PrepareDraws() => passes.Select(source => source.PrepareDraw()).OfType<SkyDraw>().ToArray();

    /// <summary>Runs the same four prepared passes through separate graphics boundaries or the production explicit shared list.</summary>
    internal void Draw(bool shared)
    {
        var work = PrepareDraws();
        if (shared) SkyDraw.Run(work);
        else foreach (var item in work) Assert.True(GraphicsCommandContext.TryRun(item.Name, item.Pipelines, true, item.Execute));
    }

    /// <summary>Reads actual simultaneous color/glow shader outputs after the owned group hands back authority.</summary>
    internal float[][] Read()
    {
        // Prefix observations must not unbind the active MRT between galaxy and stars.
        return outputs.Select(image =>
        {
            float[] pixels = new float[Size * Size * 4];
            using var binding = image.BindScope(0);
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, pixels);
            return pixels;
        }).ToArray();
    }

    /// <summary>Checks inherited state using the common hostile graphics fixture.</summary>
    internal void AssertRestored() => native.AssertRestored();

    /// <summary>Retires every borrower before its source allocations and native shader boundary.</summary>
    public void Dispose()
    {
        for (int index = owned.Count - 1; index >= 0; index--) owned[index].Dispose();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Uses the installed client representation because its interface contains methods unsupported by Moq's proxy emitter.</summary>
    private static ClientPlayer CreatePlayer()
    {
        var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var data = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        AccessTools.Field(typeof(ClientWorldPlayerData), "entityplayer").SetValue(data, new EntityPlayer());
        AccessTools.Field(typeof(ClientPlayer), "worlddata").SetValue(player, data);
        return player;
    }

    /// <summary>Preserves five distinct program input slices and the production galaxy/star pass and blending order.</summary>
    private void Draw(GraphicsCommandContext commands, int passIndex)
    {
        Contexts.Add(commands);
        commands.BeginPass(pass);
        int start = passIndex == 0 ? 0 : passIndex + 1;
        int end = passIndex == 0 ? 2 : start + 1;
        for (int index = start; index < end; index++)
        {
            commands.SetPipeline(pipelines[index]);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(index == 1 ? stars : fullscreen, index == 1 ? new(0, 6, InstanceCount: 1) : new(0, 3));
            Draws.Add(programs[index].GetType().Name);
            if (ReadPrefixes) PrefixImages.Add(Read());
        }
        commands.EndPass();
    }

    /// <summary>Records dependency construction order for deterministic reverse cleanup.</summary>
    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Add(resource);
        return resource;
    }
    #endregion
}
