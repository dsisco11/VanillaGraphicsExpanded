using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.PBR.Clouds;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks immediate post-tick mesh submission and native placement without persistent borrow tracking.</summary>
[Collection("GPU")]
public sealed class CloudNativeSimpleAdapterTests(HeadlessGLFixture fixture)
{
    private static Action<CloudRenderer, float>? tick;

    #region Public API
    /// <summary>The installed callback ticks once under selected ownership and resumes its original body after release.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedCallbackPreservesUpdateAndRestoration(bool paused)
    {
        fixture.EnsureContextValid();
        var (api,owner,_,_)=Create();
        api.SetupGet(value=>value.IsGamePaused).Returns(paused);
        api.SetupGet(value=>value.Render.FrameWidth).Returns(0);
        var uniforms=new DefaultShaderUniforms { PerceptionEffectIntensity=.25f };
        api.SetupGet(value=>value.Render.ShaderUniforms).Returns(uniforms);
        var weather=AccessTools.Field(typeof(CloudRenderer),"weatherSys").GetValue(owner) as WeatherSystemClient;
        AccessTools.Field(typeof(WeatherSystemClient),"cloudRenderer").SetValue(weather,owner);
        api.Setup(value=>value.ModLoader.GetModSystem<WeatherSystemClient>(It.IsAny<bool>())).Returns(weather!);
        int mode=2; api.Setup(value=>value.Settings.Int.Get("cloudRenderMode",0)).Returns(()=>mode);
        using var runtime=new CloudNativeRuntime(api.Object);
        Mock.Get(api.Object.Event).Raise(value=>value.LevelFinalize+=null); runtime.Update();
        var renderer=new CloudSimpleRenderer(api.Object);
        AccessTools.Field(typeof(CloudNativeRuntime),"simple").SetValue(runtime,renderer);
        Assert.True(runtime.Ownership.TryActivate(CloudMode.Simple,renderer,true));
        int ticks=0; tick=(_,_)=>ticks++;
        var harmony=new Harmony("vge.tests.cloud.simple.callback");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(CloudRenderer),nameof(CloudRenderer.CloudTick)),
                prefix:new HarmonyMethod(AccessTools.Method(typeof(CloudNativeSimpleAdapterTests),nameof(Tick))));
            Assert.NotEmpty(harmony.CreateClassProcessor(typeof(CloudSimpleDrawHook)).Patch());
            owner.OnRenderFrame(.1f,EnumRenderStage.OIT);
            Assert.Equal(paused?0:1,ticks);
            Assert.False(CloudSimpleDrawHook.Prefix(owner,.1f,EnumRenderStage.OIT));
            Assert.Equal(paused?0:2,ticks);
            var foreign=(CloudRenderer)RuntimeHelpers.GetUninitializedObject(typeof(CloudRenderer));
            Assert.True(CloudSimpleDrawHook.Prefix(foreign,.1f,EnumRenderStage.OIT));
            mode=0; Assert.True(CloudSimpleDrawHook.Prefix(owner,.1f,EnumRenderStage.OIT));
            mode=2; runtime.Update();
            owner.OnRenderFrame(.1f,EnumRenderStage.OIT);
            Assert.Equal(paused?0:3,ticks); // Original callback, still exactly one tick.
            Assert.False(runtime.Ownership.Owns(CloudMode.Simple));
            Assert.Equal(.25f,uniforms.PerceptionEffectIntensity);
            Mock.Get(api.Object.Event).Raise(value=>value.LeaveWorld+=null);
            Mock.Get(api.Object.Event).Raise(value=>value.LevelFinalize+=null); runtime.Update();
            var nextRenderer=new CloudSimpleRenderer(api.Object);
            AccessTools.Field(typeof(CloudNativeRuntime),"simple").SetValue(runtime,nextRenderer);
            Assert.True(runtime.Ownership.TryActivate(CloudMode.Simple,nextRenderer,true));
            Assert.True(CloudSimpleDrawHook.Prefix(owner,.1f,EnumRenderStage.OIT));
            var nextOwner=(CloudRenderer)RuntimeHelpers.GetUninitializedObject(typeof(CloudRenderer));
            AccessTools.Field(typeof(WeatherSystemClient),"cloudRenderer").SetValue(weather,nextOwner);
            AccessTools.Field(typeof(CloudRenderer),"renderClouds").SetValue(nextOwner,false);
            Assert.False(CloudSimpleDrawHook.Prefix(nextOwner,.1f,EnumRenderStage.OIT));
        }
        finally { harmony.UnpatchAll(harmony.Id); tick=null; }
    }

    /// <summary>Paused rendering reads retained geometry and current placement without mutating or disposing native resources.</summary>
    [Fact]
    public void CurrentMeshAndPlacementMatchNativeInputs()
    {
        var (api, owner, mesh, state) = Create();
        api.SetupGet(value => value.IsGamePaused).Returns(true);
        CloudSimpleInput? captured = null;
        CloudNativeSimpleAdapter.Render(api.Object, owner, .1f, 1, 2, value => captured = value);
        Assert.NotNull(captured); var input = captured.Value;
        Assert.Same(mesh, input.Mesh); Assert.Equal(400, input.Count);
        Assert.Equal(140.25f, input.Placement.Offset.X); Assert.Equal(180.5f, input.Placement.Offset.Z);
        Assert.Equal(96.5f, input.Placement.Offset.Y); Assert.Equal(158, input.Placement.Clock);
        Assert.Equal(8, input.Placement.WindTileX); Assert.Equal(1, input.Placement.TileX);
        state.CenterTilePos.X = 20; Assert.Equal(3, input.Placement.CenterX);
        api.SetupGet(value => value.Render.FrameWidth).Returns(0);
        CloudNativeSimpleAdapter.Render(api.Object, owner, .1f, 1, 2, _ => Assert.Fail("Zero-width draw"));
        Assert.False(mesh.Disposed);
    }

    /// <summary>A rebuild during ticking is observed immediately; pause skips ticking and unavailable drawing never skips a running update.</summary>
    [Theory]
    [InlineData(false, 800, true)]
    [InlineData(true, 800, true)]
    [InlineData(false, 0, true)]
    [InlineData(false, 800, false)]
    public void TickAndSubmissionRemainInOneCallback(bool paused, int width, bool camera)
    {
        var (api, owner, original, _) = Create();
        api.SetupGet(value => value.IsGamePaused).Returns(paused);
        api.SetupGet(value => value.Render.FrameWidth).Returns(width);
        var replacement = Mesh(); int ticks = 0, draws = 0;
        tick = (current, dt) =>
        {
            Assert.Same(owner, current); Assert.Equal(.125f, dt); ticks++;
            AccessTools.Field(typeof(CloudRenderer), "cloudTilesMeshRef").SetValue(owner, replacement);
        };
        var harmony = new Harmony("vge.tests.cloud.simple.tick");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(CloudRenderer), nameof(CloudRenderer.CloudTick)),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CloudNativeSimpleAdapterTests), nameof(Tick))));
            CloudNativeSimpleAdapter.Render(api.Object, owner, .125f, 1, camera ? 2u : null, input =>
            {
                draws++; Assert.Equal(paused ? 0 : 1, ticks);
                Assert.Same(paused ? original : replacement, input.Mesh);
            });
            Assert.Equal(paused ? 0 : 1, ticks); Assert.Equal(width == 0 || !camera ? 0 : 1, draws);
            AccessTools.Field(typeof(CloudRenderer), "renderClouds").SetValue(owner, false);
            CloudNativeSimpleAdapter.Render(api.Object, owner, .125f, 1, 2, _ => Assert.Fail("Unselected draw"));
            Assert.Equal(paused ? 0 : 1, ticks);
        }
        finally { harmony.UnpatchAll(harmony.Id); tick = null; }
        Assert.False(original.Disposed); Assert.False(replacement.Disposed);
    }

    /// <summary>Missing, uninitialized and disposed meshes omit submission without transferring disposal authority.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnavailableGeometryIsNotSubmitted(int state)
    {
        var (api, owner, original, _) = Create();
        api.SetupGet(value => value.IsGamePaused).Returns(true);
        var mesh = new Mock<MeshRef> { CallBase = true };
        mesh.SetupGet(value => value.Initialized).Returns(state == 2);
        if (state == 2) mesh.Object.Dispose();
        AccessTools.Field(typeof(CloudRenderer), "cloudTilesMeshRef").SetValue(owner, state == 0 ? null : mesh.Object);
        CloudNativeSimpleAdapter.Render(api.Object, owner, .1f, 1, 2, _ => Assert.Fail("Unavailable geometry draw"));
        Assert.False(original.Disposed);
    }
    #endregion

    #region Private
    /// <summary>Substitutes deterministic native update work without starting a weather worker.</summary>
    private static bool Tick(CloudRenderer __instance, float deltaTime) { tick!(__instance, deltaTime); return false; }

    /// <summary>Creates initialized geometry whose storage remains under the simulated native owner's authority.</summary>
    private static MeshRef Mesh()
    {
        var mesh = new Mock<MeshRef> { CallBase = true }; mesh.SetupGet(value => value.Initialized).Returns(true); return mesh.Object;
    }

    /// <summary>Constructs controlled native placement and camera inputs without creating a world or GPU allocation.</summary>
    private static (Mock<ICoreClientAPI> Api, CloudRenderer Owner, MeshRef Mesh, CloudTilesState State) Create()
    {
        var owner = (CloudRenderer)RuntimeHelpers.GetUninitializedObject(typeof(CloudRenderer));
        owner.CloudTileSize = 50; owner.CloudTileLength = 20; owner.QuantityCloudTiles = 400;
        owner.windOffsetX = .25; owner.windOffsetZ = .5;
        var state = new CloudTilesState(); state.CenterTilePos.X = 3; state.CenterTilePos.Z = 4;
        state.TileOffsetX = 1; state.TileOffsetZ = 2; state.WindTileOffsetX = 8; state.WindTileOffsetZ = 9;
        var mesh = Mesh();
        var weather = (WeatherSystemClient)RuntimeHelpers.GetUninitializedObject(typeof(WeatherSystemClient)); weather.CloudLevelRel = .5f;
        AccessTools.Field(typeof(CloudRenderer), "committedState").SetValue(owner, state);
        AccessTools.Field(typeof(CloudRenderer), "cloudTilesMeshRef").SetValue(owner, mesh);
        AccessTools.Field(typeof(CloudRenderer), "weatherSys").SetValue(owner, weather);
        AccessTools.Field(typeof(CloudRenderer), "renderClouds").SetValue(owner, true);
        var entity = new EntityPlayer(); entity.Pos.SetPos(10, 30, 20); entity.CameraPos.Y = 32;
        var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var worldData = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        AccessTools.Field(typeof(ClientWorldPlayerData), "entityplayer").SetValue(worldData, entity);
        AccessTools.Field(typeof(ClientPlayer), "worlddata").SetValue(player, worldData);
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        AccessTools.Field(typeof(CloudRenderer), "capi").SetValue(owner, api.Object);
        api.SetupGet(value => value.World.Player).Returns(player);
        api.SetupGet(value => value.World.BlockAccessor.MapSizeY).Returns(256);
        api.SetupGet(value => value.World.Calendar.TotalHours).Returns(123.5);
        api.SetupGet(value => value.Render.FrameWidth).Returns(800);
        return (api, owner, mesh, state);
    }
    #endregion
}
