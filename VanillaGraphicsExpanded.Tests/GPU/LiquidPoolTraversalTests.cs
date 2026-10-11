using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Executes patched engine traversal for visible and depth pool inputs, including original finally writes.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LiquidPoolTraversalTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    [ThreadStatic] private static Action? observe;
    #region Public API
    /// <summary>Engine dimension arithmetic, interpolation and finally restoration feed typed UBO inputs in both passes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealTraversalPreservesInputsCullingAndFinally(bool depth)
    {
        EnsureContextValid();
        TestUniformRing.EnsureFrame();
        using var storage = new LiquidPoolStorage();
        using var programs = new ComponentShaderPrograms();
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        using var lights = new VgeLightsUniformBuffer();
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var volume = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        GpuProgram program;
        if (depth) program = programs.Create<LiquidDepthShaderProgram>(owner => owner.FrameInputs = camera);
        else program = programs.Create<LiquidShaderProgram>(owner =>
        {
            owner.FrameInputs = camera; owner.LightsInputs = lights;
            owner.TerrainTexture = texture.TextureId; owner.DepthTexture = texture.TextureId;
            owner.MaterialParamsTexture = texture; owner.ShadowMapNear = texture.TextureId; owner.ShadowMapFar = texture.TextureId;
            owner.AerialRadianceTexture = volume; owner.AerialAttenuationTexture = volume;
        });
        float[] identity = [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        var moved = (float[])identity.Clone(); moved[12] = 7;
        var master = new MeshDataPoolMasterManager(null!); master.OnFrame(.125f, identity, identity);
        var dimension = new Mock<IMiniDimension>();
        dimension.SetupGet(value => value.selectionTrackingOriginalPos).Returns(new BlockPos());
        dimension.Setup(value => value.GetRenderOffset(.125f)).Returns(new FastVec3d(4, 5, 6));
        dimension.Setup(value => value.GetRenderTransformMatrix(identity, It.IsAny<Vec3d>())).Returns(moved);
        var world = new Mock<IClientWorldAccessor>(); IMiniDimension selected = dimension.Object;
        world.Setup(value => value.TryGetMiniDimension(It.IsAny<Vec3i>(), out selected)).Returns(true);
        var settings = new Mock<ISettings>(); var floats = new Mock<ISettingsClass<float>>();
        floats.SetupGet(value => value["previewTransparency"]).Returns(.35f); settings.SetupGet(value => value.Float).Returns(floats.Object);
        var render = CreateRender();
        var api = new Mock<ICoreClientAPI>(); api.SetupGet(value => value.Render).Returns(render);
        api.SetupGet(value => value.World).Returns(world.Object); api.SetupGet(value => value.Settings).Returns(settings.Object);
        api.SetupGet(value => value.Event).Returns(new Mock<IClientEventAPI>().Object);
        var manager = new MeshDataPoolManager(master, null!, api.Object, 3, 3, 1);
        AccessTools.Field(typeof(MeshDataPoolManager), "pools").SetValue(manager, new List<MeshDataPool> { storage.Pool });
        AccessTools.Field(typeof(MeshDataPool), "poolOrigin").SetValue(storage.Pool, new Vec3i(10, 20, 30));
        using var submission = new LiquidGraphicsSubmission(api.Object);
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        var player = new Vec3d(1, 2, 3);
        var harmony = new Harmony("VGE.Tests.LiquidPoolTraversal");
        harmony.CreateClassProcessor(typeof(LiquidPoolSubmissionHook)).Patch();
        LiquidPoolInputBridgeHook.Install(harmony);
        harmony.Patch(AccessTools.Method(typeof(MeshDataPool), nameof(MeshDataPool.RenderMesh)),
            postfix: new HarmonyMethod(typeof(LiquidPoolTraversalTests), nameof(ObserveDraw)));
        int draws = 0;
        try
        {
            // Unknown cull modes select the engine's Hide-only visibility branch, avoiding fabricated frustum behavior.
            Action traverse = () => manager.Render(player, "origin", (EnumFrustumCullMode)999);
            Action run = () => Assert.True(submission.Run(program, [manager], new(target, depth ? [new(0)] : [new(0), new(-1, DiscardOutput: true), new(-1, DiscardOutput: true), new(-1, DiscardOutput: true), new(-1, DiscardOutput: true), new(-1, DiscardOutput: true)]), new(), depth ? [new ColorBlendDesc()] : Enumerable.Repeat(new ColorBlendDesc(), 6).ToArray(), traverse));
            AccessTools.Field(typeof(MeshDataPool), "dimensionId").SetValue(storage.Pool, 2);
            observe = () =>
            {
                draws++;
                Assert.Equal(new float[] { 9, 18 + 2 * BlockPos.DimensionBoundary, 27, 0 }, ReadDraw()[16..]);
                Assert.Null(ShaderProgramBase.CurrentShaderProgram);
            };
            run(); Assert.Equal(1, draws);
            storage.Location.Hide = true; run(); Assert.Equal(1, draws);
            storage.Location.Hide = false;
            var locations = (List<ModelDataPoolLocation>)AccessTools.Field(typeof(MeshDataPool), "poolLocations").GetValue(storage.Pool);
            locations.Clear(); run(); Assert.Equal(1, draws); locations.Add(storage.Location);
            AccessTools.Field(typeof(MeshDataPool), "dimensionId").SetValue(storage.Pool, Vintagestory.API.Config.Dimensions.MiniDimensions);
            observe = () =>
            {
                draws++;
                var bytes = ReadDraw();
                Assert.Equal(moved, bytes[..16]);
                Assert.Equal(new float[] { 13, 23, 33, .35f }, bytes[16..]);
            };
            run(); Assert.Equal(2, draws);
            using (program.UseScope()) { Assert.Equal(identity, ReadDraw()[..16]); Assert.Equal(0, ReadDraw()[19]); }
            observe = () => throw new ArithmeticException("Draw observer failure.");
            Assert.Throws<ArithmeticException>(run);
            Assert.Null(LiquidGraphicsSubmission.ActiveInputs);
            using (program.UseScope()) { Assert.Equal(identity, ReadDraw()[..16]); Assert.Equal(0, ReadDraw()[19]); }
            dimension.Verify(value => value.GetRenderOffset(.125f), Times.Exactly(2));
            dimension.Verify(value => value.GetRenderTransformMatrix(identity, player), Times.Exactly(2));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { observe = null; harmony.UnpatchAll(harmony.Id); }
    }
    /// <summary>The same patched engine traversal outside VGE submission stages the genuine native shader.</summary>
    [Fact]
    public void UnrelatedNativeTraversalUsesNativeShader()
    {
        EnsureContextValid();
        using var storage = new LiquidPoolStorage(); AccessTools.Field(typeof(MeshDataPool), "poolOrigin").SetValue(storage.Pool, new Vec3i(5, 6, 7));
        var native = new Mock<IShaderProgram>(); var render = new Mock<IRenderAPI>();
        render.SetupGet(value => value.CurrentActiveShader).Returns(native.Object);
        var api = new Mock<ICoreClientAPI>(); api.SetupGet(value => value.Render).Returns(render.Object);
        var manager = new MeshDataPoolManager(new MeshDataPoolMasterManager(null!), null!, api.Object, 3, 3, 1);
        AccessTools.Field(typeof(MeshDataPoolManager), "pools").SetValue(manager, new List<MeshDataPool> { storage.Pool });
        var harmony = new Harmony("VGE.Tests.NativePoolInputs");
        harmony.CreateClassProcessor(typeof(LiquidPoolSubmissionHook)).Patch();
        LiquidPoolInputBridgeHook.Install(harmony);
        try
        {
            Assert.Same(native.Object, LiquidPoolInputBridgeHook.ReadInputs(render.Object));
            manager.Render(new Vec3d(1, 2, 3), "origin", (EnumFrustumCullMode)999);
            native.Verify(value => value.Uniform("origin", It.Is<Vec3f>(origin => origin.X == 4 && origin.Y == 4 && origin.Z == 4)), Times.Once);
            Assert.True(LiquidPoolSubmissionHook.Prefix(storage.Pool));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    #endregion
    #region Private
    /// <summary>Runs after the intercepted complete draw while the manager's original try/finally is still active.</summary>
    private static void ObserveDraw() => observe?.Invoke();
    /// <summary>Allocates only the engine selector carrier without constructing a game or window.</summary>
    private static RenderAPIBase CreateRender()
    {
        Type[] types;
        try { types = typeof(RenderAPIBase).Assembly.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException error) { types = error.Types.OfType<Type>().ToArray(); }
        var type = types.First(value => typeof(RenderAPIBase).IsAssignableFrom(value) && !value.IsAbstract);
        var result = (RenderAPIBase)RuntimeHelpers.GetUninitializedObject(type); GC.SuppressFinalize(result); return result;
    }
    /// <summary>Reads the actual published object block independently of retained CPU state.</summary>
    private static float[] ReadDraw()
    {
        GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.Object, out int buffer);
        GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.Object, out int offset);
        Assert.NotEqual(0, buffer); float[] bytes = new float[20];
        using var binding = StateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (nint)offset, 80, bytes); return bytes;
    }
    #endregion
}
