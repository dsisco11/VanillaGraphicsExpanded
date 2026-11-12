using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Runs real third-person body and held-item snapshot code against deterministic engine-owned state.</summary>
[Collection("CPU state")]
public sealed class NativeShadowHeldItemSnapshotTests
{
    #region Public API
    /// <summary>Both prior self-shadow flag states produce a current body and held item without mutating native pose storage.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlayerBodyAndHeldSnapshotsCaptureAndRestoreNativePose(bool initialShadowFlag)
    {
        // Arrange: prepare the exact native player renderer, full third-person animator, empty body mesh and one held item.
        using var fixture = new PlayerSnapshotFixture(initialShadowFlag);
        Assert.True(fixture.Scene.IsCurrent);
        Assert.Empty(fixture.Scene.Ranges);
        Assert.Same(fixture.Model, fixture.Renderer.ModelMat);
        Assert.Equal(initialShadowFlag, AccessTools.Field(typeof(EntityPlayer), "selfNowShadowPass").GetValue(fixture.Player));
        AssertPoseRestored(fixture);

        // Act and assert: hand identity, its original mesh, and the native attachment matrix independently withdraw currentness.
        var original = fixture.Player.LeftHandItemSlot.Itemstack;
        fixture.Player.LeftHandItemSlot.Itemstack = new ItemStack(new Item());
        Assert.False(fixture.Scene.IsCurrent);
        fixture.Player.LeftHandItemSlot.Itemstack = original;
        Assert.True(fixture.Scene.IsCurrent);

        fixture.ItemInfo.ModelRef = fixture.ReplacementMesh;
        Assert.False(fixture.Scene.IsCurrent);
        fixture.ItemInfo.ModelRef = fixture.HeldMesh;
        Assert.True(fixture.Scene.IsCurrent);

        fixture.LeftPose.AnimModelMatrix[12] += 1;
        Assert.False(fixture.Scene.IsCurrent);
        fixture.LeftPose.AnimModelMatrix[12] -= 1;
        Assert.True(fixture.Scene.IsCurrent);
        AssertPoseRestored(fixture);
    }
    #endregion

    #region Private
    /// <summary>Checks the exact renderer storage and mutable native fields captured before the first snapshot call.</summary>
    private static void AssertPoseRestored(PlayerSnapshotFixture fixture)
    {
        Assert.Same(fixture.Model, fixture.Renderer.ModelMat);
        Assert.Equal(fixture.OriginalModelContents, fixture.Renderer.ModelMat);
        Assert.Equal(fixture.OriginalBodyYaw, fixture.Renderer.bodyYawLerped);
        Assert.Equal(fixture.OriginalIntoxication,
            (float)AccessTools.Field(typeof(EntityShapeRenderer), "intoxIntensity").GetValue(fixture.Renderer)!);
        Assert.Equal(fixture.OriginalSmoothedYaw,
            (float)AccessTools.Field(typeof(EntityPlayerShapeRenderer), "smoothedBodyYaw").GetValue(fixture.Renderer)!);
        Assert.Equal(fixture.InitialShadowFlag,
            AccessTools.Field(typeof(EntityPlayer), "selfNowShadowPass").GetValue(fixture.Player));
    }

    /// <summary>Owns one already-prepared native player boundary and the production body/held snapshots built from it.</summary>
    internal sealed class PlayerSnapshotFixture : IDisposable
    {
        /// <summary>Exposes the real native player and its left held slot for currentness transitions.</summary>
        internal EntityPlayer Player { get; }
        /// <summary>Exposes the exact engine player renderer whose model matrix is restored by native capture.</summary>
        internal EntityPlayerShapeRenderer Renderer { get; }
        /// <summary>Exposes the renderer-owned matrix storage before snapshot execution.</summary>
        internal float[] Model { get; }
        /// <summary>Exposes the material snapshot whose borrowed mesh identity is checked every time.</summary>
        internal ItemRenderInfo ItemInfo { get; }
        /// <summary>Exposes the borrowed held-item mesh retained by the snapshot.</summary>
        internal MultiTextureMeshRef HeldMesh { get; }
        /// <summary>Provides a second mesh identity for a currentness mutation.</summary>
        internal MultiTextureMeshRef ReplacementMesh { get; }
        /// <summary>Exposes the native left attachment pose used by matrix capture.</summary>
        internal AttachmentPointAndPose LeftPose { get; }
        /// <summary>Owns the production entity-scene snapshot until this fixture is disposed.</summary>
        internal NativeShadowEntityScene Scene { get; }
        /// <summary>Exposes the native preparation certificate used by the production entity scene.</summary>
        internal NativeShadowEntityPreparation Preparation { get; }
        /// <summary>Stores the renderer matrix values before production snapshot construction can invoke native pose capture.</summary>
        internal float[] OriginalModelContents { get; private set; } = [];
        /// <summary>Stores the native body yaw before the first shadow pose capture.</summary>
        internal float OriginalBodyYaw { get; private set; }
        /// <summary>Stores the native intoxication field before the first shadow pose capture.</summary>
        internal float OriginalIntoxication { get; private set; }
        /// <summary>Stores the native smoothed yaw before the first shadow pose capture.</summary>
        internal float OriginalSmoothedYaw { get; private set; }
        /// <summary>Retains the pre-capture value used to verify the native shadow flag is restored.</summary>
        internal bool InitialShadowFlag { get; }

        /// <summary>Creates real player matrices and held attachment state without a game loop or geometry allocation.</summary>
        internal PlayerSnapshotFixture(bool initialShadowFlag, MultiTextureMeshRef? bodyMesh = null,
            MultiTextureMeshRef? heldMesh = null, double positionY = 100, int firstCommand = 0,
            bool neutralWarp = false)
        {
            InitialShadowFlag = initialShadowFlag;
            var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
            var render = new Mock<IRenderAPI> { DefaultValue = DefaultValue.Mock };
            var world = new Mock<IClientWorldAccessor> { DefaultValue = DefaultValue.Mock };
            Player = new TestPlayer();
            Player.Api = api.Object;
            Player.World = world.Object;
            Player.AnimManager = new Mock<IAnimationManager>().Object;
            Player.Pos.SetPos(10, positionY, 20);
            Player.Pos.Pitch = MathF.PI;
            Player.Pos.Roll = .13f;
            Player.BodyYaw = .35f;
            Player.WalkPitch = .1f;
            Player.CameraPos.Set(10, positionY, 20);
            Player.LocalEyePos.Set(0, 1.6, 0);
            Player.Properties.Client.Size = .8f;
            Player.Properties.Client.Shape = new CompositeShape { rotateX = 12, rotateY = 23, rotateZ = -8 };
            Player.LeftHandItemSlot.Itemstack = new ItemStack(new Item());

            var clientPlayer = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
            var playerData = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
            AccessTools.Field(typeof(ClientWorldPlayerData), "entityplayer").SetValue(playerData, Player);
            AccessTools.Field(typeof(ClientPlayer), "worlddata").SetValue(clientPlayer, playerData);
            clientPlayer.OverrideCameraMode = EnumCameraMode.ThirdPerson;
            world.SetupGet(value => value.Player).Returns(clientPlayer);
            world.Setup(value => value.PlayerByUid(It.IsAny<string>())).Returns(clientPlayer);
            Player.CameraPos.Set(10, positionY, 20);

            var uniforms = new DefaultShaderUniforms
            {
                PlayerPos = new Vec3f(), TimeCounter = 1, WindWaveCounter = 2, WindWaveCounterHighFreq = 3,
                WaterWaveCounter = 4, WindSpeed = 5, GlobalWorldWarp = neutralWarp ? 0 : 6,
                GlitchWaviness = neutralWarp ? 0 : 7, WindWaveIntensity = neutralWarp ? 0 : 8,
                WaterWaveIntensity = neutralWarp ? 0 : 9, PerceptionEffectIntensity = 0
            };
            api.SetupGet(value => value.World).Returns(world.Object);
            api.SetupGet(value => value.Render).Returns(render.Object);
            api.SetupGet(value => value.Settings.Float["fpHandsYOffset"]).Returns(.1f);
            render.SetupGet(value => value.ShaderUniforms).Returns(uniforms);
            var perception = new PerceptionEffects(api.Object);
            render.SetupGet(value => value.PerceptionEffects).Returns(perception);
            render.SetupGet(value => value.CurrentProjectionMatrix).Returns(Mat4f.Create());
            render.SetupGet(value => value.CameraMatrixOriginf).Returns(Mat4f.Create());

            var animator = new Mock<IAnimator>();
            var matrices = new Matrixf().Values;
            animator.SetupGet(value => value.MaxJointId).Returns(1);
            animator.SetupGet(value => value.Matrices).Returns(matrices);
            LeftPose = new AttachmentPointAndPose
            {
                AnimModelMatrix = new Matrixf().Translate(.1f, 1.2f, -.2f).RotateZ(-.6f).Values,
                AttachPoint = new AttachmentPoint { PosX = 4, PosY = 9, PosZ = 3, RotationX = 20, RotationY = 30, RotationZ = -10 }
            };
            var rightPose = new AttachmentPointAndPose
            {
                AnimModelMatrix = Mat4f.Create(), AttachPoint = new AttachmentPoint { PosX = 12, PosY = 8 }
            };
            animator.Setup(value => value.GetAttachmentPointPose("LeftHand")).Returns(() => LeftPose);
            animator.Setup(value => value.GetAttachmentPointPose("RightHand")).Returns(rightPose);
            Player.TpAnimManager.Animator = animator.Object;
            Player.SelfFpAnimManager.Animator = animator.Object;

            Renderer = (EntityPlayerShapeRenderer)RuntimeHelpers.GetUninitializedObject(typeof(EntityPlayerShapeRenderer));
            Renderer.entity = Player; Renderer.capi = api.Object; Renderer.ModelMat = Mat4f.Create(); Renderer.ModelMat[13] = -1000;
            Renderer.bodyYawLerped = Player.BodyYaw; Renderer.nowSwivelRad = .08f;
            AccessTools.Field(typeof(EntityShapeRenderer), "intoxIntensity").SetValue(Renderer, .375f);
            AccessTools.Field(typeof(EntityPlayerShapeRenderer), "entityPlayer").SetValue(Renderer, Player);
            AccessTools.Field(typeof(EntityPlayerShapeRenderer), "eagent").SetValue(Renderer, Player);
            AccessTools.Field(typeof(EntityPlayerShapeRenderer), "player").SetValue(Renderer, clientPlayer);
            AccessTools.Field(typeof(EntityPlayerShapeRenderer), "renderMode").SetValue(Renderer, (RenderMode)2);
            AccessTools.Field(typeof(EntityPlayerShapeRenderer), "smoothedBodyYaw").SetValue(Renderer, Player.BodyYaw);
            AccessTools.Field(typeof(EntityPlayer), "selfNowShadowPass").SetValue(Player, initialShadowFlag);
            Model = Renderer.ModelMat;
            OriginalModelContents = (float[])Model.Clone();
            OriginalBodyYaw = Renderer.bodyYawLerped;
            OriginalIntoxication = (float)AccessTools.Field(typeof(EntityShapeRenderer), "intoxIntensity").GetValue(Renderer)!;
            OriginalSmoothedYaw = (float)AccessTools.Field(typeof(EntityPlayerShapeRenderer), "smoothedBodyYaw").GetValue(Renderer)!;
            bodyMesh ??= EmptyMesh();
            AccessTools.Field(typeof(EntityPlayerShapeRenderer), "thirdPersonMeshRef").SetValue(Renderer, bodyMesh);
            HeldMesh = heldMesh ?? EmptyMesh(); ReplacementMesh = EmptyMesh();
            var transform = new ModelTransform().EnsureDefaultValues();
            ItemInfo = new ItemRenderInfo { Transform = transform, ModelRef = HeldMesh };
            render.Setup(value => value.GetItemStackRenderInfo(It.IsAny<ItemSlot>(), It.IsAny<EnumItemRenderTarget>(), It.IsAny<float>()))
                .Returns(ItemInfo);

            var members = new Dictionary<long, EntityRenderer> { [Player.EntityId] = Renderer };
            Preparation = new NativeShadowEntityPreparation(4, new ShadowSceneChanges(8));
            Preparation.Begin(members);
            // Model this fixture at the already-prepared native frame boundary. Entity preparation's visible/offscreen
            // call counts are covered separately; this fixture exercises the real body and held snapshot consumers.
            ((HashSet<EntityRenderer>)AccessTools.Field(typeof(NativeShadowEntityPreparation), "prepared")
                .GetValue(Preparation)!).Add(Renderer);
            Preparation.End(nativeFailed: false);
            Scene = new NativeShadowEntityScene(Preparation, new ShadowDrawSettings(), firstCommand, viewCount: 1);
        }

        #region Public API
        /// <summary>Releases borrowed scene snapshots after all currentness assertions finish.</summary>
        public void Dispose() => Scene.Dispose();
        #endregion

        #region Private
        /// <summary>Creates a live native texture-mesh owner with no submeshes or owned geometry.</summary>
        private static MultiTextureMeshRef EmptyMesh()
        {
            var mesh = (MultiTextureMeshRef)RuntimeHelpers.GetUninitializedObject(typeof(MultiTextureMeshRef));
            mesh.meshrefs = [];
            mesh.textureids = [];
            return mesh;
        }
        #endregion
    }

    /// <summary>Supplies independent empty hands and the current animation manager for the native player fixture.</summary>
    private sealed class TestPlayer : EntityPlayer
    {
        /// <summary>Returns the native left held-item slot.</summary>
        public override ItemSlot LeftHandItemSlot => left;
        /// <summary>Returns the native right held-item slot.</summary>
        public override ItemSlot RightHandItemSlot => right;
        /// <summary>Returns the animation manager consumed by offscreen entity preparation.</summary>
        public override IAnimationManager AnimManager { get => animation!; set => animation = value; }

        private readonly ItemSlot left = new DummySlot();
        private readonly ItemSlot right = new DummySlot();
        private IAnimationManager? animation;

        /// <summary>Initializes client shape properties required by the installed native model-matrix method.</summary>
        internal TestPlayer() => Properties = new EntityProperties { Client = new EntityClientProperties([], new()) { Size = 1 } };
    }
    #endregion
}
