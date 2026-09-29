using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.HeldLighting;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Serializes temporary patches of installed engine collection and renderer methods.</summary>
[CollectionDefinition("Held lighting", DisableParallelization = true)]
public sealed class HeldLightingCollection;

/// <summary>Exercises real engine emission conversion, patched collection, and current attachment transforms.</summary>
[Collection("Held lighting")]
public sealed class HeldLightTests
{
    private static EngineFixture? active;

    #region Coordinates and source ownership
    /// <summary>Checks attachment rotation, item origin and nonuniform scale against installed RenderItem IL.</summary>
    [Fact]
    public void AttachmentCompositionMatchesInstalledItemRenderer()
    {
        using var fixture = new EngineFixture();
        fixture.AttachRenderer();
        var renderer = fixture.Renderer!;
        renderer.ModelMat = new Matrixf().Translate(3, 4, 5).RotateY(0.3f).Values;
        var pose = new AttachmentPointAndPose
        {
            AnimModelMatrix = new Matrixf().Translate(0.1f, 1.2f, -0.2f).RotateZ(-0.6f).Values,
            AttachPoint = new AttachmentPoint { PosX = 4, PosY = 9, PosZ = 3, RotationX = 20, RotationY = 30, RotationZ = -10 }
        };
        var transform = new ModelTransform().EnsureDefaultValues();
        transform.Origin = new FastVec3f(0.2f, 0.3f, 0.4f);
        transform.Translation = new FastVec3f(0.1f, 0.2f, -0.1f);
        transform.Rotation = new FastVec3f(15, -35, 10);
        transform.ScaleXYZ = new FastVec3f(0.8f, 1.2f, 0.7f);
        var point = new Vec4f(0.5f, 1, 0.5f, 1);
        var harmony = new Harmony("VGE.Tests.HeldLight.Transform");
        var method = AccessTools.Method(typeof(EntityShapeRenderer), "RenderItem");
        try
        {
            // Run the actual installed matrix-building prefix, stopping before the first shader operation.
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(HeldLightTests), nameof(OnlyItemTransform)));
            method.Invoke(renderer, [0f, false, fixture.Player.Right.Itemstack, pose,
                new ItemRenderInfo { Transform = transform, ModelRef = Empty<MultiTextureMeshRef>() }]);
            var matrix = (Matrixf)AccessTools.Field(typeof(EntityShapeRenderer), "ItemModelMat").GetValue(renderer)!;
            Vec4f expected = matrix.TransformVector(point);
            Vec4f actual = HeldLightAttachment.Compose(renderer.ModelMat, pose, transform, point);
            Assert.Equal(expected.X, actual.X, 5);
            Assert.Equal(expected.Y, actual.Y, 5);
            Assert.Equal(expected.Z, actual.Z, 5);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>Retains engine matrix setup and its argument guards while excluding all drawing and particles.</summary>
    private static IEnumerable<CodeInstruction> OnlyItemTransform(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, "tex"))
            {
                yield return new CodeInstruction(OpCodes.Ret).WithLabels(instruction.labels.ToArray());
                yield break;
            }
            yield return instruction;
        }
        throw new InvalidOperationException("Installed item transform boundary changed.");
    }

    /// <summary>Large world coordinates must be translated in double precision before conversion to shader floats.</summary>
    [Fact]
    public void ViewPositionPreservesSubBlockOffsetsAndNeighborEntries()
    {
        double[] view = Mat4d.Create();
        Mat4d.RotateY(view, view, Math.PI / 2);
        Mat4d.Translate(view, view, -30_000_000, -128, 30_000_000);
        float[] result = Enumerable.Repeat(-999f, 9).ToArray();
        HeldLightSources.WriteViewPosition(result, 1, view, new Vec3d(30_000_000.25, 129.5, -29_999_999.5));
        Assert.Equal(-999, result[0]);
        Assert.Equal(-999, result[8]);
        Assert.Equal(0.5f, result[3], 5);
        Assert.Equal(1.5f, result[4], 5);
        Assert.Equal(-0.25f, result[5], 5);
    }

    /// <summary>Hand projection correction must match the engine's full matrix unprojection.</summary>
    [Theory]
    [InlineData(50, 70)]
    [InlineData(90, 55)]
    [InlineData(70, 70)]
    public void FirstPersonProjectionMatchesParticlePath(float normalDegrees, float handDegrees)
    {
        float[] view = Mat4f.Create();
        Mat4f.RotateY(view, view, 0.45f);
        float[] normal = Mat4f.Create();
        float[] hand = Mat4f.Create();
        Mat4f.Perspective(normal, normalDegrees * GameMath.DEG2RAD, 1.7f, 0.1f, 1000f);
        Mat4f.Perspective(hand, handDegrees * GameMath.DEG2RAD, 1.7f, 0.1f, 1000f);
        var input = new Vec4f(0.4f, 1.1f, -0.8f, 1);
        Vec4f clip = new Matrixf(hand).Mul(view).TransformVector(input);
        Vec4f expected = new Matrixf(view).Invert().Mul(new Matrixf(normal).Invert()).TransformVector(clip);
        Vec4f actual = HeldLightAttachment.ReprojectHand(input, view, normal, handDegrees * GameMath.DEG2RAD);
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }

    /// <summary>Each hand retains its own engine RGB conversion; innate emission is never moved to a hand.</summary>
    [Fact]
    public void SplitsHandsAndPreservesBaseEmissionAndEngineColors()
    {
        using var fixture = new EngineFixture();
        fixture.Player.LightHsv = [3, 2, 7];
        HeldLightSources.Begin(fixture.Effects);
        HeldLightSources.AddEntityLight(fixture.Effects, fixture.Player.LightHsv, fixture.Player);
        Assert.Equal(3, fixture.Game.shUniforms.PointLightsCount);
        Assert.Equal(100, fixture.Game.shUniforms.PointLights3[1]);
        Assert.Equal(101.2f, fixture.Game.shUniforms.PointLights3[4], 4);
        Assert.Equal(101.2f, fixture.Game.shUniforms.PointLights3[7], 4);
        float[] actual = fixture.Game.shUniforms.PointLightColors3.Take(9).ToArray();
        fixture.Game.shUniforms.PointLightsCount = 0;
        fixture.AddOriginal([3, 2, 7], fixture.Player.Pos);
        fixture.AddOriginal([5, 4, 12], fixture.Player.Pos);
        fixture.AddOriginal([30, 3, 9], fixture.Player.Pos);
        Assert.Equal(actual, fixture.Game.shUniforms.PointLightColors3.Take(9));
    }

    /// <summary>No emitting hand preserves spawn/innate behavior; unrelated entity lights retain their coordinates.</summary>
    [Fact]
    public void DarkHandsAndNonPlayersPassThroughUnchanged()
    {
        using var fixture = new EngineFixture();
        fixture.Player.Right.Itemstack = null;
        fixture.Player.Left.Itemstack = null;
        HeldLightSources.Begin(fixture.Effects);
        HeldLightSources.AddEntityLight(fixture.Effects, [33, 7, 10], fixture.Player);
        var dropped = new EntityItem();
        dropped.Pos.SetPos(3, 4, 5);
        HeldLightSources.AddEntityLight(fixture.Effects, [5, 4, 12], dropped);
        Assert.Equal(2, fixture.Game.shUniforms.PointLightsCount);
        Assert.Equal(new float[] { 10, 100, 20, 3, 4, 5 }, fixture.Game.shUniforms.PointLights3.Take(6));
    }

    /// <summary>Splitting hands must honor the engine capacity and must not record a rejected second hand.</summary>
    [Fact]
    public void HonorsDynamicLightLimit()
    {
        using var fixture = new EngineFixture();
        Set(fixture.Effects, "maxDynLights", 1);
        HeldLightSources.Begin(fixture.Effects);
        HeldLightSources.AddEntityLight(fixture.Effects, fixture.Player.LightHsv, fixture.Player);
        Assert.Equal(1, fixture.Game.shUniforms.PointLightsCount);
        HeldLightSources.Complete(fixture.Entities, 0.016f);
        Assert.Equal(1, fixture.Game.shUniforms.PointLightsCount);
    }
    #endregion

    #region Installed engine lifecycle
    /// <summary>Partial collection must be replaced with vanilla output without losing unrelated patch owners.</summary>
    [Fact]
    public void CollectionFailureDisablesOnlyHeldLightingAndRebuildsVanillaList()
    {
        using var fixture = new EngineFixture();
        fixture.InstallCollection();
        fixture.Patch(AccessTools.Method(typeof(HeldLightSources), "AddHand"), nameof(FailLeftHand));
        var perception = new CountingPerception(fixture.Api.Object);
        fixture.Render.Object.PerceptionEffects.RegisterPerceptionEffect(perception, "counter");
        fixture.Render.Object.PerceptionEffects.TriggerEffect("counter", 1, true);
        active = fixture;
        try
        {
            fixture.Collect();
            Assert.False(HeldLightSystem.Enabled);
            Assert.Equal(1, fixture.Game.shUniforms.PointLightsCount);
            Assert.Equal(new float[] { 10, 100, 20 }, fixture.Game.shUniforms.PointLights3.Take(3));
            Assert.Equal(1, perception.BeforeUpdates);
            string error = Assert.Single(fixture.Errors);
            Assert.Contains("injected collection failure", error);
            Assert.Contains("System.InvalidOperationException", error);
            Assert.Contains("AddEntityLight", error);
            AssertHeldPatchesRemoved();
            Assert.Contains("VGE.Tests.HeldLight", Harmony.GetPatchInfo(
                AccessTools.Method(typeof(SystemRenderEntities), "OnBeforeRender"))!.Owners);
            fixture.Collect();
            Assert.Equal(1, fixture.Game.shUniforms.PointLightsCount);
            Assert.Single(fixture.Errors);
        }
        finally { active = null; }
    }

    /// <summary>Failure after a hand update restores the whole vanilla list, including lights excluded by split capacity.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttachmentFailureRollsBackCurrentFrameAndContainsRecoveryErrors(bool failRecovery)
    {
        using var fixture = new EngineFixture();
        fixture.InstallCollection();
        fixture.AttachRenderer();
        Set(fixture.Effects, "maxDynLights", 2);
        var standalone = new Mock<IPointLight>();
        standalone.SetupGet(light => light.Pos).Returns(new Vec3d(3, 4, 5));
        standalone.SetupGet(light => light.Color).Returns(new Vec3f(0.3f, 0.5f, 0.7f));
        Set(fixture.Game, "pointlights", new List<IPointLight> { standalone.Object });
        var perception = new CountingPerception(fixture.Api.Object);
        fixture.Render.Object.PerceptionEffects.RegisterPerceptionEffect(perception, "counter");
        fixture.Render.Object.PerceptionEffects.TriggerEffect("counter", 1, true);
        fixture.Render.Setup(render => render.GetItemStackRenderInfo(It.IsAny<ItemSlot>(), It.IsAny<EnumItemRenderTarget>(), It.IsAny<float>()))
            .Returns((ItemSlot slot, EnumItemRenderTarget target, float dt) =>
            {
                if (ReferenceEquals(slot, fixture.Player.Left))
                {
                    fixture.FailQuery = failRecovery;
                    throw new InvalidOperationException("injected attachment failure");
                }
                return new ItemRenderInfo { Transform = new ModelTransform().EnsureDefaultValues() };
            });
        active = fixture;
        try
        {
            fixture.Collect();
            Assert.Equal(2, fixture.Game.shUniforms.PointLightsCount);
            fixture.Game.MvMatrix.Translate(1000, 1000, 1000);
            fixture.Player.selfNowShadowPass = true;
            AccessTools.Method(typeof(SystemRenderEntities), "OnBeforeRender").Invoke(fixture.Entities, [0.016f]);
            Assert.False(HeldLightSystem.Enabled);
            Assert.True(fixture.Player.selfNowShadowPass);
            Assert.Equal(1, fixture.Game.MvMatrix.Count);
            Assert.Equal(1000, fixture.Game.MvMatrix.Top[13]);
            Assert.Equal(1, perception.BeforeUpdates);
            Assert.Contains("injected attachment failure", fixture.Errors[0]);
            Assert.Contains("HeldLightAttachment.Resolve", fixture.Errors[0]);
            AssertHeldPatchesRemoved();
            if (failRecovery)
            {
                Assert.Equal(0, fixture.Game.shUniforms.PointLightsCount);
                Assert.Equal(2, fixture.Errors.Count);
                Assert.Contains("injected engine query failure", fixture.Errors[1]);
            }
            else
            {
                Assert.Equal(2, fixture.Game.shUniforms.PointLightsCount);
                Assert.Equal(new float[] { 10, 100, 20, 3, 4, 5 }, fixture.Game.shUniforms.PointLights3.Take(6));
                Assert.Single(fixture.Errors);
            }
            fixture.FailQuery = false;
            fixture.Collect();
            Assert.Equal(2, fixture.Game.shUniforms.PointLightsCount);
        }
        finally { active = null; }
    }

    /// <summary>Engine failures outside held-light work remain visible and are not misreported as subsystem failures.</summary>
    [Fact]
    public void UnrelatedEngineExceptionIsNotSuppressed()
    {
        using var fixture = new EngineFixture();
        fixture.InstallCollection();
        fixture.FailQuery = true;
        active = fixture;
        try
        {
            var error = Assert.Throws<TargetInvocationException>(fixture.Collect);
            Assert.Contains("injected engine query failure", error.InnerException!.Message);
            Assert.True(HeldLightSystem.Enabled);
            Assert.Empty(fixture.Errors);
        }
        finally { active = null; }
    }

    /// <summary>A changed engine call shape must roll back hooks already installed before the failing transpiler.</summary>
    [Fact]
    public void InstallationFailureRemovesPartialPatchesAndPreservesForeignOwner()
    {
        var foreign = new Harmony("VGE.Tests.HeldLight.Foreign");
        var method = AccessTools.Method(typeof(SystemRenderPlayerEffects), "onBeforeRender");
        var errors = new List<string>();
        try
        {
            foreign.Patch(method, transpiler: new HarmonyMethod(typeof(HeldLightTests), nameof(ChangeCollectionCall)) { priority = Priority.First });
            HeldLightSystem.Start(errors.Add);
            Assert.False(HeldLightSystem.Enabled);
            Assert.Contains("Expected one engine entity-light call", Assert.Single(errors));
            AssertHeldPatchesRemoved();
            Assert.Contains(foreign.Id, Harmony.GetPatchInfo(method)!.Owners);
            Assert.Empty(typeof(HeldLightHooks).GetCustomAttributes<HarmonyAttribute>());
        }
        finally
        {
            HeldLightSystem.Stop();
            foreign.UnpatchAll(foreign.Id);
        }
    }

    /// <summary>Checks real Harmony ownership rather than only the subsystem's enabled flag.</summary>
    private static void AssertHeldPatchesRemoved()
        => Assert.DoesNotContain(Harmony.GetAllPatchedMethods(), method =>
            Harmony.GetPatchInfo(method)?.Owners.Contains(HeldLightSystem.PatchId) == true);

    /// <summary>Fails after the first hand has been admitted, before the second is published.</summary>
    private static void FailLeftHand(bool right)
    {
        if (!right) throw new InvalidOperationException("injected collection failure");
    }

    /// <summary>Simulates an incompatible collection call while keeping the foreign transpiler valid.</summary>
    private static IEnumerable<CodeInstruction> ChangeCollectionCall(IEnumerable<CodeInstruction> instructions)
    {
        var add = AccessTools.Method(typeof(SystemRenderPlayerEffects), "AddPointLight", [typeof(byte[]), typeof(EntityPos)]);
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(add))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(HeldLightTests), nameof(AlternateEngineCall));
            }
            yield return instruction;
        }
    }

    /// <summary>Supplies a differently named but equivalent engine-light call for startup compatibility testing.</summary>
    private static void AlternateEngineCall(SystemRenderPlayerEffects effects, byte[] light, EntityPos position)
        => AccessTools.Method(typeof(SystemRenderPlayerEffects), "AddPointLight", [typeof(byte[]), typeof(EntityPos)])
            .Invoke(effects, [light, position]);

    /// <summary>Runs installed collection and a patched animation boundary with a fresh pose and no graphics calls.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void PatchedCollectionResolvesCurrentPoseAndLeavesRendererStateUntouched(int mode, bool remote)
    {
        using var fixture = new EngineFixture();
        fixture.InstallCollection();
        fixture.AttachRenderer(mode);
        if (remote)
        {
            var observer = new TestPlayer();
            observer.CameraPos.Set(500, 80, 700);
            Set(fixture.Game.player.WorldData, "entityplayer", observer);
        }
        var standalone = new Mock<IPointLight>();
        standalone.SetupGet(light => light.Pos).Returns(new Vec3d(3, 4, 5));
        standalone.SetupGet(light => light.Color).Returns(new Vec3f(0.3f, 0.5f, 0.7f));
        Set(fixture.Game, "pointlights", new List<IPointLight> { standalone.Object });
        var effect = new CountingPerception(fixture.Api.Object);
        fixture.Render.Object.PerceptionEffects.RegisterPerceptionEffect(effect, "test");
        fixture.Render.Object.PerceptionEffects.TriggerEffect("test", 1, true);
        active = fixture;
        try
        {
            fixture.Collect();
            Assert.Equal(3, fixture.Game.shUniforms.PointLightsCount);
            float before = fixture.Game.shUniforms.PointLights3[1];
            float[] originalModel = fixture.Renderer!.ModelMat;
            float[] originalValues = (float[])originalModel.Clone();
            fixture.Player.selfNowShadowPass = true;
            fixture.Game.MvMatrix.Translate(1000, 1000, 1000);
            _ = fixture.Game.CurrentModelViewMatrixd; // Overwrite the engine's shared matrix scratch array.
            // The animation method replacement updates a pose. Production's postfix must observe that update.
            AccessTools.Method(typeof(SystemRenderEntities), "OnBeforeRender").Invoke(fixture.Entities, [0.016f]);
            float after = fixture.Game.shUniforms.PointLights3[1];
            Assert.NotEqual(before, after);
            Assert.Equal(103.5f, after, 4);
            Assert.Equal(103.5f, fixture.Game.shUniforms.PointLights3[4], 4);
            Assert.NotEqual(fixture.Game.shUniforms.PointLights3[0], fixture.Game.shUniforms.PointLights3[3]);
            Assert.Same(originalModel, fixture.Renderer.ModelMat);
            Assert.Equal(originalValues, originalModel);
            Assert.Equal(0.2f, fixture.Renderer.bodyYawLerped);
            Assert.True(fixture.Player.selfNowShadowPass);
            Assert.Equal(new float[] { 3, 4, 5 }, fixture.Game.shUniforms.PointLights3.Skip(6).Take(3));
            Assert.Equal(new float[] { 0.7f, 0.5f, 0.3f }, fixture.Game.shUniforms.PointLightColors3.Skip(6).Take(3));
            Assert.Equal(0, effect.Applications);
            fixture.Render.Object.PerceptionEffects.ApplyToTpPlayer(fixture.Player, originalModel);
            Assert.Equal(1, effect.Applications);
            // Remove the emitters next frame: old indices and attachment work cannot survive the reset.
            fixture.Player.Right.Itemstack = null;
            fixture.Player.Left.Itemstack = null;
            Mat4d.Identity(fixture.Game.MvMatrix.Top);
            fixture.Collect();
            Assert.Equal(1, fixture.Game.shUniforms.PointLightsCount);
            AccessTools.Method(typeof(SystemRenderEntities), "OnBeforeRender").Invoke(fixture.Entities, [0.016f]);
            Assert.Equal(1, fixture.Game.shUniforms.PointLightsCount);
            Assert.Equal(new float[] { 3, 4, 5 }, fixture.Game.shUniforms.PointLights3.Take(3));
        }
        finally { active = null; }
    }

    /// <summary>Retains the real collection predicate while supplying a deterministic nearby entity set.</summary>
    private static bool Nearby(ActionConsumable<Entity> __3, ref Entity[] __result)
    {
        if (active!.FailQuery) throw new InvalidOperationException("injected engine query failure");
        __result = __3(active!.Player) ? [active.Player] : [];
        return false;
    }

    /// <summary>Represents this frame's animation update while leaving the production postfix installed.</summary>
    private static bool Animate()
    {
        active!.RightPose.AnimModelMatrix[13] = 2;
        active.LeftPose.AnimModelMatrix[13] = 2;
        return false;
    }

    /// <summary>Sets installed engine private fields used by the window-free fixture.</summary>
    private static void Set(object instance, string name, object? value) => AccessTools.Field(instance.GetType(), name).SetValue(instance, value);

    /// <summary>Allocates a native-bound engine owner without running its window/session constructor.</summary>
    private static T Empty<T>() where T : class
    {
        var value = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(value);
        return value;
    }
    #endregion

    #region Engine fixture
    /// <summary>Uses installed HSV conversion and renderer transforms with mocked world and graphics services.</summary>
    private sealed class EngineFixture : IDisposable
    {
        internal readonly ClientMain Game = Empty<ClientMain>();
        internal readonly SystemRenderPlayerEffects Effects = Empty<SystemRenderPlayerEffects>();
        internal readonly SystemRenderEntities Entities = Empty<SystemRenderEntities>();
        internal readonly TestPlayer Player = new();
        internal readonly Mock<IRenderAPI> Render = new();
        internal readonly Mock<ICoreClientAPI> Api = new() { DefaultValue = DefaultValue.Mock };
        internal readonly AttachmentPointAndPose RightPose = new() { AttachPoint = new AttachmentPoint { PosX = 12, PosY = 8 } };
        internal readonly AttachmentPointAndPose LeftPose = new() { AttachPoint = new AttachmentPoint { PosX = 4, PosY = 8 } };
        internal EntityPlayerShapeRenderer? Renderer;
        private readonly Harmony harmony = new("VGE.Tests.HeldLight");
        internal readonly List<string> Errors = new();
        internal bool FailQuery;

        /// <summary>Creates engine light storage and a player with independently colored emitting hands.</summary>
        internal EngineFixture()
        {
            Game.shUniforms = new DefaultShaderUniforms();
            Game.MvMatrix = new StackMatrix4();
            Game.MvMatrix.PushIdentity();
            Set(Game, "tmpMatrixd", new double[16]);
            Game.WorldMap = Empty<ClientWorldMap>();
            Set(Game.WorldMap, "hueLevels", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
            Set(Game.WorldMap, "satLevels", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
            Set(Game.WorldMap, "BlockLightLevels", Enumerable.Range(0, 256).Select(i => i / 255f).ToArray());
            Set(Game, "EntityRenderers", new Dictionary<long, EntityRenderer>());
            Set(Game, "pointlights", new List<IPointLight>());
            var engineApi = Empty<ClientCoreAPI>();
            var engineRender = Empty<RenderAPIGame>();
            Set(engineApi, "renderapi", engineRender);
            Set(Game, "api", engineApi);
            Set(Effects, "game", Game);
            Set(Effects, "maxDynLights", 32);
            Set(Effects, "inval", new Vec4d());
            Set(Effects, "outval", new Vec4d());
            Set(Effects, "outval3", new Vec3f());
            Set(Entities, "game", Game);
            var world = new Mock<IClientWorldAccessor>() { DefaultValue = DefaultValue.Mock };
            // IPlayer has an internal abstract member which Castle cannot implement; use the engine owner.
            var clientPlayer = Empty<ClientPlayer>();
            var data = Empty<ClientWorldPlayerData>();
            Set(data, "entityplayer", Player);
            Set(clientPlayer, "worlddata", data);
            Game.player = clientPlayer;
            clientPlayer.OverrideCameraMode = EnumCameraMode.ThirdPerson;
            world.SetupGet(w => w.Player).Returns(clientPlayer);
            world.Setup(w => w.PlayerByUid(It.IsAny<string>())).Returns(clientPlayer);
            world.SetupGet(w => w.Calendar.TotalHours).Returns(100);
            Api.SetupGet(a => a.World).Returns(world.Object);
            Api.SetupGet(a => a.Render).Returns(Render.Object);
            var perception = new PerceptionEffects(Api.Object);
            Render.SetupGet(r => r.PerceptionEffects).Returns(perception);
            Render.SetupGet(r => r.ShaderUniforms).Returns(Game.shUniforms);
            Set(engineRender, "perceptionEffects", perception);
            Player.World = world.Object;
            Player.Api = Api.Object;
            Player.Pos.SetPos(10, 100, 20);
            Player.CameraPos.Set(10, 100, 20);
            Player.LocalEyePos.Set(0, 1.6, 0);
            Player.Right.Itemstack = new ItemStack(new Item { LightHsv = new byte[] { 5, 4, 12 } });
            Player.Left.Itemstack = new ItemStack(new Item { LightHsv = new byte[] { 30, 3, 9 } });
        }

        /// <summary>Invokes the installed engine HSV conversion for comparison.</summary>
        internal void AddOriginal(byte[] hsv, EntityPos position) => AccessTools.Method(typeof(SystemRenderPlayerEffects),
            "AddPointLight", [typeof(byte[]), typeof(EntityPos)]).Invoke(Effects, [hsv, position]);

        /// <summary>Installs production hooks, replacing only native session dependencies.</summary>
        internal void InstallCollection()
        {
            HeldLightSystem.Start(Errors.Add);
            Assert.True(HeldLightSystem.Enabled, string.Join("\n", Errors));
            Patch(AccessTools.Method(typeof(Vintagestory.Common.GameMain), "GetEntitiesAround", [typeof(Vec3d), typeof(float), typeof(float), typeof(ActionConsumable<Entity>)]), nameof(Nearby));
            Patch(AccessTools.Method(typeof(SystemRenderEntities), "OnBeforeRender"), nameof(Animate));
        }

        /// <summary>Builds a standard renderer whose current poses and item transforms are fully deterministic.</summary>
        internal void AttachRenderer(int mode = 2)
        {
            Renderer = Empty<EntityPlayerShapeRenderer>();
            Renderer.entity = Player;
            Renderer.capi = Api.Object;
            Renderer.ModelMat = Mat4f.Create();
            Renderer.ModelMat[13] = -1000; // Last frame's matrix must never be used.
            Renderer.bodyYawLerped = 0.2f;
            Set(Renderer, "entityPlayer", Player);
            Set(Renderer, "eagent", Player);
            Set(Renderer, "player", Api.Object.World.Player);
            Set(Renderer, "renderMode", (RenderMode)mode);
            ((ClientPlayer)Api.Object.World.Player).OverrideCameraMode = mode == 2 ? EnumCameraMode.ThirdPerson : EnumCameraMode.FirstPerson;
            Player.Pos.Pitch = MathF.PI;
            Api.SetupGet(api => api.Settings.Int["fpHandsFoV"]).Returns(70);
            float[] projection = Mat4f.Create();
            Mat4f.Perspective(projection, 70 * GameMath.DEG2RAD, 1.7f, 0.1f, 1000f);
            Render.SetupGet(render => render.CurrentProjectionMatrix).Returns(projection);
            Render.SetupGet(render => render.CameraMatrixOriginf).Returns(Mat4f.Create());
            Set(Renderer, "smoothedBodyYaw", 0.2f);
            var animator = new Mock<IAnimator>();
            animator.Setup(a => a.GetAttachmentPointPose("RightHand")).Returns(RightPose);
            animator.Setup(a => a.GetAttachmentPointPose("LeftHand")).Returns(LeftPose);
            var animations = new Mock<IAnimationManager>();
            animations.SetupGet(a => a.Animator).Returns(animator.Object);
            Player.Animation = animations.Object;
            Render.Setup(r => r.GetItemStackRenderInfo(It.IsAny<ItemSlot>(), It.IsAny<EnumItemRenderTarget>(), It.IsAny<float>()))
                .Returns(new ItemRenderInfo { Transform = new ModelTransform().EnsureDefaultValues() });
            Set(Game, "EntityRenderers", new Dictionary<long, EntityRenderer> { [Player.EntityId] = Renderer });
        }

        /// <summary>Runs the installed collection loop including its predicate and production transpiler.</summary>
        internal void Collect() => AccessTools.Method(typeof(SystemRenderPlayerEffects), "onBeforeRender").Invoke(Effects, [0.016f]);

        /// <summary>Installs a named fixture prefix.</summary>
        internal void Patch(MethodInfo method, string prefix) => harmony.Patch(method, prefix: new HarmonyMethod(typeof(HeldLightTests), prefix));

        /// <summary>Removes all temporary engine patches even after a failed assertion.</summary>
        public void Dispose()
        {
            HeldLightSystem.Stop();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    /// <summary>Detects unintended invocation of pose-mutating perception callbacks during snapshot evaluation.</summary>
    private sealed class CountingPerception(ICoreClientAPI api) : PerceptionEffect(api)
    {
        internal int Applications;
        internal int BeforeUpdates;
        /// <summary>Counts frame updates to detect accidental replay during light-list recovery.</summary>
        public override void OnBeforeGameRender(float dt) => BeforeUpdates++;
        /// <summary>Counts calls without needing a fully simulated shared pose tree.</summary>
        public override void ApplyToTpPlayer(EntityPlayer player, float[] matrix, float? intensity = null) => Applications++;
    }

    /// <summary>Supplies independent slots while retaining the installed EntityPlayer.LightHsv getter.</summary>
    private sealed class TestPlayer : EntityPlayer
    {
        internal readonly ItemSlot Right = new DummySlot();
        internal readonly ItemSlot Left = new DummySlot();
        internal IAnimationManager? Animation;
        /// <summary>Initializes model properties needed by the engine transform.</summary>
        internal TestPlayer() => Properties = new EntityProperties { Client = new EntityClientProperties([], new()) { Size = 1 } };
        /// <summary>Provides the test right hand.</summary>
        public override ItemSlot RightHandItemSlot => Right;
        /// <summary>Provides the test left hand.</summary>
        public override ItemSlot LeftHandItemSlot => Left;
        /// <summary>Provides the current test animation pose without a running game simulation.</summary>
        public override IAnimationManager AnimManager { get => Animation!; set => Animation = value; }
    }
    #endregion
}
