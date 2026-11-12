using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Checks native scene ownership and exception-safe restoration of the live player pose state.</summary>
[Collection("CPU state")]
public sealed class NativeShadowSceneTests
{
    #region Public API
    /// <summary>An empty complete scene withdraws when a native renderer enters its captured membership.</summary>
    [Fact]
    public void EmptySceneRejectsEntityEnteringAfterCapture()
    {
        // Arrange: compose the production terrain and entity owners from a current empty native world and renderer map.
        using var scene = new EmptySceneFixture();
        Assert.True(scene.Scene.IsCurrent);

        // Act: add an entity renderer after the complete empty-scene snapshot has been frozen.
        var entity = new TestEntity(81);
        scene.Members.Add(entity.EntityId, CountingRenderer.Create(entity));

        // Assert: the same retained dictionary is checked directly, so new casters invalidate empty coverage.
        Assert.False(scene.Preparation.IsCurrent);
        Assert.False(scene.Scene.IsCurrent);
    }

    /// <summary>A captured empty scene becomes current again only after the native membership returns to its exact snapshot.</summary>
    [Fact]
    public void EmptySceneRechecksMembershipAfterInsertionIsRemoved()
    {
        // Arrange: freeze a complete empty native scene backed by the current mutable renderer dictionary.
        using var scene = new EmptySceneFixture();
        Assert.True(scene.Scene.IsCurrent);
        var entity = new TestEntity(82);
        scene.Members.Add(entity.EntityId, CountingRenderer.Create(entity));
        Assert.False(scene.Scene.IsCurrent);

        // Act: remove the unprepared renderer from that same dictionary.
        scene.Members.Remove(entity.EntityId);

        // Assert: empty membership matches the still-retained original publication again.
        Assert.True(scene.Preparation.IsCurrent);
        Assert.True(scene.Scene.IsCurrent);
    }

    /// <summary>Offscreen dropped-item preparation and empty-stack scene capture occur once and retain currentness.</summary>
    [Fact]
    public void OffscreenGroundItemIsPreparedOnceAndCapturedAsEmpty()
    {
        // Arrange: bind one native dropped-item renderer to a current camera and finite frame uniforms with an empty slot.
        var entity = new TestEntity(84);
        entity.Pos.SetPos(10, 20, 30);
        var uniforms = Uniforms();
        var cameraEntity = new EntityPlayer();
        cameraEntity.CameraPos.Set(4, 5, 6);
        var cameraPlayer = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var playerData = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        AccessTools.Field(typeof(ClientWorldPlayerData), "entityplayer").SetValue(playerData, cameraEntity);
        AccessTools.Field(typeof(ClientPlayer), "worlddata").SetValue(cameraPlayer, playerData);
        var clientWorld = new Mock<IClientWorldAccessor>();
        clientWorld.SetupGet(value => value.Player).Returns(cameraPlayer);
        var clientApi = new Mock<ICoreClientAPI>();
        clientApi.SetupGet(value => value.World).Returns(clientWorld.Object);
        clientApi.SetupGet(value => value.Render.ShaderUniforms).Returns(uniforms);
        var renderer = (EntityItemRenderer)RuntimeHelpers.GetUninitializedObject(typeof(EntityItemRenderer));
        AccessTools.Field(typeof(EntityRenderer), "entity").SetValue(renderer, entity);
        AccessTools.Field(typeof(EntityRenderer), "capi").SetValue(renderer, clientApi.Object);
        var members = new Dictionary<long, EntityRenderer> { [entity.EntityId] = renderer };
        var preparation = new NativeShadowEntityPreparation(4, new ShadowSceneChanges(8));
        preparation.Begin(members);

        // Act: request offscreen preparation twice, then freeze the actual native entity scene.
        var firstAnimation = preparation.AnimationFor(entity, .016f);
        var repeatedAnimation = preparation.AnimationFor(entity, .032f);
        Assert.NotNull(firstAnimation);
        Assert.Same(firstAnimation, repeatedAnimation);
        Assert.Null(firstAnimation.Animator);
        preparation.End(nativeFailed: false);
        using var scene = new NativeShadowEntityScene(preparation, new ShadowDrawSettings(), firstCommand: 0, viewCount: 1);

        // Assert: only one offscreen owner pass ran and the no-stack item contributes no fabricated draw interval.
        Assert.True(preparation.Complete);
        Assert.Equal(1, preparation.ExtraPreparations);
        Assert.Same(renderer, Assert.Single(preparation.Renderers));
        Assert.Empty(scene.Ranges);
        Assert.True(scene.IsCurrent);

        // Act and assert: the retained native membership is rechecked while the scene remains alive.
        members.Remove(entity.EntityId);
        Assert.False(preparation.IsCurrent);
        Assert.False(scene.IsCurrent);
    }

    /// <summary>The native player pose owner restores its array, smoothing, yaw and prior shadow flag when capture fails.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlayerPoseCaptureRestoresStateOnNativeFailure(bool initialShadowFlag)
    {
        // Arrange: use the exact native renderer type with no client service so its real IsSelf pose path faults.
        var entity = new EntityPlayer();
        var renderer = (EntityPlayerShapeRenderer)RuntimeHelpers.GetUninitializedObject(typeof(EntityPlayerShapeRenderer));
        AccessTools.Field(typeof(EntityRenderer), "entity").SetValue(renderer, entity);
        float[] model = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 7, 8, 9, 1];
        renderer.ModelMat = model;
        float yaw = 1.25f, intoxication = .375f, smoothing = -.625f;
        AccessTools.Field(typeof(EntityShapeRenderer), "bodyYawLerped").SetValue(renderer, yaw);
        AccessTools.Field(typeof(EntityShapeRenderer), "intoxIntensity").SetValue(renderer, intoxication);
        AccessTools.Field(typeof(EntityPlayerShapeRenderer), "smoothedBodyYaw").SetValue(renderer, smoothing);
        AccessTools.Field(typeof(EntityPlayer), "selfNowShadowPass").SetValue(entity, initialShadowFlag);
        float[] original = (float[])model.Clone();
        var isSelf = AccessTools.PropertyGetter(typeof(EntityPlayerShapeRenderer), "IsSelf")!;

        // Act: the production capture enters its restoration scope before resolving the missing native client service.
        Assert.Throws<System.Reflection.TargetInvocationException>(() => isSelf.Invoke(renderer, null));
        Assert.Throws<NullReferenceException>(() => NativeShadowShapePose.Capture(renderer));

        // Assert: finally restores the exact model array and every native field that capture may rewrite.
        Assert.Same(model, renderer.ModelMat);
        Assert.Equal(original, renderer.ModelMat);
        Assert.Equal(yaw, AccessTools.Field(typeof(EntityShapeRenderer), "bodyYawLerped").GetValue(renderer));
        Assert.Equal(intoxication, AccessTools.Field(typeof(EntityShapeRenderer), "intoxIntensity").GetValue(renderer));
        Assert.Equal(smoothing, AccessTools.Field(typeof(EntityPlayerShapeRenderer), "smoothedBodyYaw").GetValue(renderer));
        Assert.Equal(initialShadowFlag, AccessTools.Field(typeof(EntityPlayer), "selfNowShadowPass").GetValue(entity));
    }

    /// <summary>The real dropped-item shadow matrix path captures a pose and restores its native model array.</summary>
    [Fact]
    public void GroundItemPoseCaptureExecutesNativeShadowBranchWithoutMutatingRendererStorage()
    {
        // Arrange: supply the native renderer's camera, item transform and mutable model array without running visible drawing.
        var cameraEntity = new EntityPlayer();
        cameraEntity.CameraPos.Set(4, 5, 6);
        var cameraPlayer = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var playerData = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        AccessTools.Field(typeof(ClientWorldPlayerData), "entityplayer").SetValue(playerData, cameraEntity);
        AccessTools.Field(typeof(ClientPlayer), "worlddata").SetValue(cameraPlayer, playerData);
        var clientWorld = new Mock<IClientWorldAccessor>();
        clientWorld.SetupGet(value => value.Player).Returns(cameraPlayer);
        var clientApi = new Mock<ICoreClientAPI>();
        clientApi.SetupGet(value => value.World).Returns(clientWorld.Object);

        var entity = (EntityItem)RuntimeHelpers.GetUninitializedObject(typeof(EntityItem));
        var renderer = (EntityItemRenderer)RuntimeHelpers.GetUninitializedObject(typeof(EntityItemRenderer));
        AccessTools.Field(typeof(EntityRenderer), "entity").SetValue(renderer, entity);
        AccessTools.Field(typeof(EntityRenderer), "capi").SetValue(renderer, clientApi.Object);
        AccessTools.Field(typeof(EntityItemRenderer), "entityitem").SetValue(renderer, entity);
        AccessTools.Field(typeof(EntityItemRenderer), "lerpedPos").SetValue(renderer, new Vec3d(1, 2, 3));
        float[] original = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 7, 8, 9, 1];
        AccessTools.Field(typeof(EntityItemRenderer), "ModelMat").SetValue(renderer, original);
        var info = (ItemRenderInfo)RuntimeHelpers.GetUninitializedObject(typeof(ItemRenderInfo));
        var transform = new ModelTransform();
        transform.EnsureDefaultValues();
        AccessTools.Field(typeof(ItemRenderInfo), "Transform").SetValue(info, transform);

        // Act: execute the installed native LoadModelMatrix shadow branch through the production snapshot adapter.
        var captured = NativeShadowItemPose.Capture(renderer, info);

        // Assert: a finite native pose was returned while the renderer keeps its exact prior storage and values.
        Assert.Equal(16, captured.Length);
        Assert.All(captured, value => Assert.True(float.IsFinite(value)));
        Assert.Same(original, AccessTools.Field(typeof(EntityItemRenderer), "ModelMat").GetValue(renderer));
        Assert.Equal(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 7, 8, 9, 1 }, original);
    }

    /// <summary>Unknown entity renderer types fail scene capture rather than becoming silent missing casters.</summary>
    [Fact]
    public void EntitySceneRejectsUnadaptedRendererFamily()
    {
        // Arrange: publish one prepared renderer whose geometry owner has no complete native shadow adapter.
        var entity = new TestEntity(83);
        var renderer = CountingRenderer.Create(entity);
        var members = new Dictionary<long, EntityRenderer> { [entity.EntityId] = renderer };
        var preparation = new NativeShadowEntityPreparation(4, new ShadowSceneChanges(8));
        preparation.Begin(members);
        preparation.AnimationFor(entity, .016f);
        preparation.End(nativeFailed: false);

        // Act and assert: the native scene refuses to call unsupported renderer geometry complete.
        Assert.Throws<NotSupportedException>(() => new NativeShadowEntityScene(preparation,
            new ShadowDrawSettings(), firstCommand: 0, viewCount: 1));
    }
    #endregion

    #region Private
    /// <summary>Creates finite frame deformation inputs for the ground-item scene snapshot.</summary>
    private static DefaultShaderUniforms Uniforms() => new()
    {
        PlayerPos = new Vec3f(), TimeCounter = 1, WindWaveCounter = 2, WindWaveCounterHighFreq = 3,
        WaterWaveCounter = 4, WindSpeed = 5, GlobalWorldWarp = 6, GlitchWaviness = 7,
        WindWaveIntensity = 8, WaterWaveIntensity = 9, PerceptionEffectIntensity = 10
    };

    /// <summary>Owns the current empty native world and both production scene adapters for one test.</summary>
    private sealed class EmptySceneFixture : IDisposable
    {
        private readonly NativeShadowWorldAdapter worldOwner;

        /// <summary>Exposes the actual dictionary retained by the entity preparation certificate.</summary>
        internal Dictionary<long, EntityRenderer> Members { get; } = new();
        /// <summary>Exposes the preparation owner used by the composed scene.</summary>
        internal NativeShadowEntityPreparation Preparation { get; }
        /// <summary>Exposes the complete production scene certificate.</summary>
        internal NativeShadowScene Scene { get; }

        /// <summary>Creates current empty native publications without introducing pool or entity registries.</summary>
        internal EmptySceneFixture()
        {
            var map = (ClientWorldMap)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldMap));
            AccessTools.Field(typeof(ClientWorldMap), "chunks").SetValue(map, new Dictionary<long, ClientChunk>());
            AccessTools.Field(typeof(ClientWorldMap), "chunksLock").SetValue(map, new object());
            worldOwner = new NativeShadowWorldAdapter(map, 4, 8, 4, 8);
            var publication = worldOwner.Capture();
            if (!publication.Complete) throw new InvalidOperationException(publication.Failure ?? "Empty world capture failed.");

            var uniforms = new DefaultShaderUniforms
            {
                PlayerPos = new Vec3f(), TimeCounter = 1, WindWaveCounter = 2, WindWaveCounterHighFreq = 3,
                WaterWaveCounter = 4, WindSpeed = 5, GlobalWorldWarp = 6, GlitchWaviness = 7,
                WindWaveIntensity = 8, WaterWaveIntensity = 9, PerceptionEffectIntensity = 10
            };
            var renderer = (ChunkRenderer)RuntimeHelpers.GetUninitializedObject(typeof(ChunkRenderer));
            var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
            platform.UseSSBOs = false;
            NativeShadowTerrainFields.Platform(renderer) = platform;
            NativeShadowTerrainFields.Table(renderer) = new MeshDataPoolManager[1][];
            NativeShadowTerrainFields.Textures(renderer) = [1];

            var settings = new ShadowDrawSettings();
            var terrain = new NativeShadowTerrainScene(renderer, worldOwner, publication,
                new NativeShadowTerrainDetail(0, 0, 1, 1), null!, uniforms, waving: false, settings);
            Preparation = new NativeShadowEntityPreparation(4, new ShadowSceneChanges(8));
            Preparation.Begin(Members);
            Preparation.End(nativeFailed: false);
            var entities = new NativeShadowEntityScene(Preparation, settings, firstCommand: 0, viewCount: 1);
            Scene = new NativeShadowScene(terrain, entities);
        }

        #region Public API
        /// <summary>Releases the composed scene before its borrowed world publication owner.</summary>
        public void Dispose()
        {
            Scene.Dispose();
            worldOwner.Dispose();
        }
        #endregion
    }

    /// <summary>Provides stable native identity for an entity membership transition.</summary>
    private sealed class TestEntity : Entity
    {
        /// <summary>Sets the key consumed by native render preparation.</summary>
        internal TestEntity(long id) => EntityId = id;
    }

    /// <summary>Represents an unadapted renderer family without creating camera or GPU state.</summary>
    private sealed class CountingRenderer : EntityRenderer
    {
        private CountingRenderer() : base(null!, null!) { }

        #region Public API
        /// <inheritdoc />
        public override void BeforeRender(float dt) { }
        /// <inheritdoc />
        public override void Dispose() { }
        #endregion

        #region Internal API
        /// <summary>Creates a renderer shell bound to its native entity reference.</summary>
        internal static CountingRenderer Create(Entity entity)
        {
            var result = (CountingRenderer)RuntimeHelpers.GetUninitializedObject(typeof(CountingRenderer));
            AccessTools.Field(typeof(EntityRenderer), "entity").SetValue(result, entity);
            return result;
        }
        #endregion
    }
    #endregion
}
