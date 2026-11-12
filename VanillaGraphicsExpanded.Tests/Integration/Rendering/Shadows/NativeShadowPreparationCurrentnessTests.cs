using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Checks native material, pose and renderer-membership snapshots at their live engine boundaries.</summary>
[Collection("CPU state")]
public sealed class NativeShadowPreparationCurrentnessTests
{
    #region Public API
    /// <summary>Every native deformation uniform and the perception mode withdraws its frozen warp snapshot.</summary>
    [Fact]
    public void WarpSnapshotRejectsEveryChangedNativeDeformationInput()
    {
        // Arrange: independently vary every input that can alter ordinary terrain or animated entity geometry.
        Action<DefaultShaderUniforms>[] changes =
        [
            value => value.PlayerPos = new Vec3f(4, 5, 6),
            value => value.TimeCounter += 1,
            value => value.WindWaveCounter += 1,
            value => value.WindWaveCounterHighFreq += 1,
            value => value.WaterWaveCounter += 1,
            value => value.WindSpeed += 1,
            value => value.GlobalWorldWarp += 1,
            value => value.GlitchWaviness += 1,
            value => value.WindWaveIntensity += 1,
            value => value.WaterWaveIntensity += 1,
            value => value.PerceptionEffectIntensity += 1,
            value => value.PerceptionEffectId += 1
        ];

        // Act and assert: each changed snapshot source is rejected even when native mesh storage remains untouched.
        foreach (var change in changes)
        {
            var uniforms = Uniforms();
            var snapshot = new NativeShadowWarpSnapshot(uniforms);
            Assert.True(snapshot.IsCurrent);
            change(uniforms);
            Assert.False(snapshot.IsCurrent);
        }
    }

    /// <summary>Rejects nonfinite deformation values before they can enter a native material block.</summary>
    [Fact]
    public void WarpSnapshotRejectsNonfiniteInputs()
    {
        // Arrange: the global native warp value participates in both shader displacement and conservative bounds.
        var uniforms = Uniforms();
        uniforms.GlobalWorldWarp = float.NaN;

        // Act and assert: incomplete material geometry input is rejected at snapshot capture.
        Assert.Throws<InvalidOperationException>(() => new NativeShadowWarpSnapshot(uniforms));
    }

    /// <summary>Uses the qualified third-person player animator and scopes the native self-shadow pose flag.</summary>
    [Fact]
    public void NativeShapeSnapshotUsesThirdPersonAnimatorAndScopesSelfShadowPose()
    {
        // Arrange: inspect the installed native type and the exact adapters used by shadow material preparation.
        var currentAnimator = AccessTools.Method(typeof(NativeShadowShapeSnapshot), "CurrentAnimator")!;
        var getterIl = Instructions(currentAnimator);
        var thirdPersonGetter = AccessTools.PropertyGetter(typeof(EntityPlayer), nameof(EntityPlayer.TpAnimManager));
        var ordinaryGetter = AccessTools.PropertyGetter(typeof(Entity), nameof(Entity.AnimManager));
        var captureIl = Instructions(AccessTools.Method(typeof(NativeShadowShapePose), nameof(NativeShadowShapePose.Capture))!);
        var shadowField = AccessTools.Field(typeof(EntityPlayer), "selfNowShadowPass");

        // Assert: player snapshots select the third-person animation manager, ordinary entities keep their native getter,
        // and transform capture brackets the native pose call with self-shadow state writes.
        Assert.NotNull(thirdPersonGetter);
        Assert.NotNull(ordinaryGetter);
        Assert.Contains(getterIl, instruction => instruction.Calls(thirdPersonGetter));
        Assert.Contains(getterIl, instruction => instruction.Calls(ordinaryGetter));
        Assert.Contains(captureIl, instruction => instruction.LoadsField(shadowField));
        Assert.True(captureIl.Count(instruction => instruction.StoresField(shadowField)) >= 2);
        Assert.Contains(captureIl, instruction => instruction.Calls(
            AccessTools.Method(typeof(EntityPlayerShapeRenderer), "loadModelMatrixForPlayer")));
    }

    /// <summary>Removal and same-count renderer replacement invalidate the exact published native frame membership.</summary>
    [Fact]
    public void PreparationRejectsRemovedRendererReplacedAtSameDictionaryCount()
    {
        // Arrange: publish one prepared native renderer, then retain the same mutable dictionary object.
        var entity = new TestEntity(51);
        var original = CountingRenderer.Create(entity);
        var current = new Dictionary<long, EntityRenderer> { [entity.EntityId] = original };
        var preparation = Prepare(current, entity);
        long frame = preparation.Frame;
        Assert.True(preparation.Matches(original, frame));

        // Act: replace the native renderer under the same entity key without changing the dictionary count.
        var replacement = CountingRenderer.Create(entity);
        current[entity.EntityId] = replacement;

        // Assert: the published renderer alone remains prepared; same-count replacement is not accepted as current.
        Assert.Equal(1, current.Count);
        Assert.True(preparation.Complete);
        Assert.False(preparation.Matches(original, frame));
        Assert.False(preparation.Matches(replacement, frame));
    }

    /// <summary>Removal followed by an unrelated same-count entry cannot preserve an old renderer certificate.</summary>
    [Fact]
    public void PreparationRejectsRemovalAndUnrelatedSameCountEntry()
    {
        // Arrange: capture the prepared renderer under its original native entity ID.
        var entity = new TestEntity(61);
        var original = CountingRenderer.Create(entity);
        var current = new Dictionary<long, EntityRenderer> { [entity.EntityId] = original };
        var preparation = Prepare(current, entity);
        long frame = preparation.Frame;

        // Act: remove its key and add a renderer for an unrelated entity, preserving the published count.
        var otherEntity = new TestEntity(62);
        var other = CountingRenderer.Create(otherEntity);
        current.Remove(entity.EntityId);
        current.Add(otherEntity.EntityId, other);

        // Assert: neither the missing original nor an unprepared newcomer can consume the prior frame.
        Assert.Equal(1, current.Count);
        Assert.True(preparation.Complete);
        Assert.False(preparation.Matches(original, frame));
        Assert.False(preparation.Matches(other, frame));
    }
    #endregion

    #region Private
    /// <summary>Creates finite nonzero frame values for every native warp and perception field.</summary>
    private static DefaultShaderUniforms Uniforms() => new()
    {
        PlayerPos = new Vec3f(1, 2, 3), TimeCounter = 4, WindWaveCounter = 5,
        WindWaveCounterHighFreq = 6, WaterWaveCounter = 7, WindSpeed = 8,
        GlobalWorldWarp = 9, GlitchWaviness = 10, WindWaveIntensity = 11,
        WaterWaveIntensity = 12, PerceptionEffectIntensity = 13, PerceptionEffectId = 2
    };

    /// <summary>Loads original Harmony instructions from one installed method for source-level ABI qualification.</summary>
    private static CodeInstruction[] Instructions(System.Reflection.MethodBase method)
    {
        var generator = new DynamicMethod("VgeNativeShadowCurrentnessProbe", typeof(void), Type.EmptyTypes,
            typeof(NativeShadowPreparationCurrentnessTests), true).GetILGenerator();
        return PatchProcessor.GetOriginalInstructions(method, generator).ToArray();
    }

    /// <summary>Captures one native renderer through the regular visible/offscreen preparation boundary.</summary>
    private static NativeShadowEntityPreparation Prepare(Dictionary<long, EntityRenderer> current, Entity entity)
    {
        var preparation = new NativeShadowEntityPreparation(4, new ShadowSceneChanges(8));
        preparation.Begin(current);
        preparation.AnimationFor(entity, .016f);
        preparation.End(nativeFailed: false);
        return preparation;
    }

    /// <summary>Provides a stable entity identity with no unrelated world services.</summary>
    private sealed class TestEntity : Entity
    {
        /// <summary>Sets the native dictionary key used by the frame preparation owner.</summary>
        internal TestEntity(long id) => EntityId = id;
    }

    /// <summary>Records renderer preparation without initializing native graphics or camera services.</summary>
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
        /// <summary>Creates a native renderer shell bound to the supplied real entity reference.</summary>
        internal static CountingRenderer Create(Entity entity)
        {
            var renderer = (CountingRenderer)RuntimeHelpers.GetUninitializedObject(typeof(CountingRenderer));
            AccessTools.Field(typeof(EntityRenderer), "entity").SetValue(renderer, entity);
            return renderer;
        }
        #endregion
    }
    #endregion
}
