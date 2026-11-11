using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Checks offscreen preparation against actual entity APIs and installed native animation IL.</summary>
[Collection("CPU state")]
public sealed class NativeShadowEntityPreparationTests
{
    #region Public API
    /// <summary>Installed preparation rewriting preserves one visible call and routes native animation through one guarded wrapper.</summary>
    [Fact]
    public void NativeFrameIlRewritesOnlyTheQualifiedPreparationCalls()
    {
        // Arrange: Read the installed SystemRenderEntities body from the supported engine assembly.
        var target = AccessTools.Method(typeof(SystemRenderEntities), "OnBeforeRender");
        var generator = new DynamicMethod("VgeShadowEntityIlProbe", typeof(void), System.Array.Empty<System.Type>(), typeof(NativeShadowEntityPreparationTests), true).GetILGenerator();
        var original = PatchProcessor.GetOriginalInstructions(target, generator).ToArray();
        var nativeBefore = AccessTools.Method(typeof(EntityRenderer), nameof(EntityRenderer.BeforeRender));
        var nativeAnimation = AccessTools.PropertyGetter(typeof(Vintagestory.API.Common.Entities.Entity), nameof(Vintagestory.API.Common.Entities.Entity.AnimManager));
        var nativeAdvance = AccessTools.Method(typeof(Vintagestory.API.Common.IAnimationManager), nameof(Vintagestory.API.Common.IAnimationManager.OnClientFrame));

        // Act: Apply the production rewrite and count its replacement calls.
        var rewritten = NativeShadowEntityHooks.Rewrite(original.Select(instruction => new CodeInstruction(instruction))).ToArray();
        var prepare = AccessTools.Method(typeof(NativeShadowEntityHooks), "Prepare");
        var animation = AccessTools.Method(typeof(NativeShadowEntityHooks), "Animation");
        var advance = AccessTools.Method(typeof(NativeShadowEntityHooks), "Advance");

        // Assert: The installed frame owns exactly one call at each preparation boundary and no original calls bypass it.
        Assert.Equal(1, rewritten.Count(instruction => instruction.Calls(prepare)));
        Assert.Equal(1, rewritten.Count(instruction => instruction.Calls(animation)));
        Assert.Equal(1, rewritten.Count(instruction => instruction.Calls(advance)));
        Assert.DoesNotContain(rewritten, instruction => instruction.Calls(nativeBefore));
        Assert.DoesNotContain(rewritten, instruction => instruction.Calls(nativeAnimation));
        Assert.DoesNotContain(rewritten, instruction => instruction.Calls(nativeAdvance));
    }

    /// <summary>An offscreen entity is prepared before its animation advances and visibility is restored afterward.</summary>
    [Fact]
    public void OffscreenEntityPreparationPrecedesSingleAnimationAdvance()
    {
        // Arrange: Give one native renderer a current animator while marking its entity offscreen.
        var changes = new ShadowSceneChanges(8);
        var preparation = new NativeShadowEntityPreparation(4, changes);
        var entity = new TestEntity(41) { IsRendered = false };
        var renderer = CountingRenderer.Create(entity);
        var animation = new Mock<Vintagestory.API.Common.IAnimationManager>();
        var calls = 0;
        animation.Setup(manager => manager.OnClientFrame(It.IsAny<float>())).Callback(() =>
        {
            calls++;
            Assert.True(entity.IsRendered);
            Assert.Equal(1, renderer.BeforeRenderCalls);
        });
        entity.AnimManager = animation.Object;
        var current = new Dictionary<long, EntityRenderer> { [entity.EntityId] = renderer };
        preparation.Begin(current);

        // Act: The native visible path and shadow demand share preparation; repeating animation does not double-advance.
        var selected = preparation.AnimationFor(entity, .016f);
        preparation.PrepareVisible(renderer, .016f);
        Assert.Same(animation.Object, selected);
        preparation.Advance(entity, animation.Object, .016f);
        preparation.Advance(entity, animation.Object, .016f);
        preparation.End(false);

        // Assert: One preparation and one animation tick produce a complete current snapshot and restore camera visibility.
        Assert.True(preparation.Complete);
        Assert.Equal(1, preparation.ExtraPreparations);
        Assert.Equal(1, renderer.BeforeRenderCalls);
        Assert.Equal(1, calls);
        Assert.False(entity.IsRendered);
        Assert.Same(renderer, Assert.Single(preparation.Renderers));
        animation.Verify(manager => manager.OnClientFrame(It.IsAny<float>()), Times.Once);
    }

    /// <summary>An offscreen entity without an animator still receives its current renderer preparation.</summary>
    [Fact]
    public void MissingAnimatorDoesNotPreventRendererPreparation()
    {
        // Arrange: Publish a native renderer for an entity whose animator is absent.
        var preparation = new NativeShadowEntityPreparation(2, new ShadowSceneChanges(4));
        var entity = new TestEntity(7);
        var renderer = CountingRenderer.Create(entity);
        preparation.Begin(new Dictionary<long, EntityRenderer> { [entity.EntityId] = renderer });

        // Act: Request renderer preparation while the entity has a manager with no active Animator.
        var animation = preparation.AnimationFor(entity, .02f);
        preparation.End(false);

        // Assert: The manager exists without an Animator, and renderer preparation still completes.
        Assert.NotNull(animation);
        Assert.Null(animation.Animator);
        Assert.Equal(1, renderer.BeforeRenderCalls);
        Assert.True(preparation.Complete);
        Assert.Same(renderer, Assert.Single(preparation.Renderers));
    }

    /// <summary>Preparation-capacity exhaustion prevents a complete snapshot and advances broad invalidation for unchanged and removed entities.</summary>
    [Fact]
    public void CapacityFailureAndUnboundedEntityChangesInvalidatePriorEpoch()
    {
        // Arrange: Keep one entity beyond the configured complete-snapshot allowance.
        var changes = new ShadowSceneChanges(4);
        var preparation = new NativeShadowEntityPreparation(1, changes);
        var first = new TestEntity(1);
        var firstRenderer = CountingRenderer.Create(first);
        var extra = new TestEntity(2);
        var extraRenderer = CountingRenderer.Create(extra);
        preparation.Begin(new Dictionary<long, EntityRenderer> { [first.EntityId] = firstRenderer, [extra.EntityId] = extraRenderer });

        // Act: Fail the oversized scene, then observe an unchanged frame and a disappearance.
        preparation.End(false);
        bool overflowComplete = preparation.Complete;
        var overflow = changes.Capture(0, 1);
        preparation.Begin(new Dictionary<long, EntityRenderer> { [first.EntityId] = firstRenderer });
        preparation.AnimationFor(first, .016f);
        preparation.End(false);
        var unchanged = changes.Capture(overflow.Sequence, overflow.Epoch);
        preparation.Begin(new Dictionary<long, EntityRenderer>());
        preparation.End(false);
        var disappeared = changes.Capture(unchanged.Sequence, unchanged.Epoch);

        // Assert: Neither capacity truncation nor unbounded deformation history can retain old page dependencies.
        Assert.False(overflowComplete);
        Assert.True(overflow.Complete);
        Assert.True(overflow.RequiresBroadInvalidation);
        Assert.True(preparation.Complete);
        Assert.True(unchanged.RequiresBroadInvalidation);
        Assert.True(disappeared.RequiresBroadInvalidation);
        Assert.Empty(preparation.Renderers);
    }
    #endregion

    #region Private
    /// <summary>Provides the minimum native entity state consumed by the preparation owner.</summary>
    private sealed class TestEntity : Entity
    {
        /// <summary>Sets a stable identity while leaving all unused world services absent.</summary>
        internal TestEntity(long id) => EntityId = id;
    }

    /// <summary>Counts the native renderer preparation boundary without invoking camera or graphics services.</summary>
    private sealed class CountingRenderer : EntityRenderer
    {
        private CountingRenderer() : base(null!, null!) { }
        internal int BeforeRenderCalls { get; private set; }

        #region Public API
        /// <summary>Records the call that the installed engine normally gates by visible culling.</summary>
        public override void BeforeRender(float dt) => BeforeRenderCalls++;

        /// <summary>Releases no resources because this uninitialized fixture never creates renderer-owned graphics objects.</summary>
        public override void Dispose() { }
        #endregion

        #region Internal API
        /// <summary>Creates a renderer shell with the actual entity field and no unused game services.</summary>
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
