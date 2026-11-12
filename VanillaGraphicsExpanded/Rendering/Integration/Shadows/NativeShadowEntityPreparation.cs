using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Tracks one native entity preparation interval and supplements offscreen preparation before animation.</summary>
internal sealed class NativeShadowEntityPreparation
{
    private readonly int capacity;
    private readonly ShadowSceneChanges changes;
    private readonly HashSet<EntityRenderer> prepared = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IAnimationManager> animated = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<EntityItemRenderer, ItemRenderInfo?> itemMaterials = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyDictionary<long, EntityRenderer>? renderers;
    private IReadOnlyDictionary<long, EntityRenderer>? published;
    private bool open, failed;

    #region Public API
    internal long Frame { get; private set; }
    internal bool Complete { get; private set; }
    internal int ExtraPreparations { get; private set; }
    internal ImmutableArray<EntityRenderer> Renderers { get; private set; } = [];
    /// <summary>Rejects native membership changes even when the captured scene contained no entities.</summary>
    internal bool IsCurrent
    {
        get
        {
            if (!Complete || open || published is null || published.Count != Renderers.Length) return false;
            foreach (var renderer in Renderers)
                if (!published.TryGetValue(renderer.entity.EntityId, out var current) || !ReferenceEquals(current, renderer)) return false;
            return true;
        }
    }

    /// <summary>Charges all retained renderer references to a finite preparation allowance.</summary>
    internal NativeShadowEntityPreparation(int capacity, ShadowSceneChanges changes)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        this.changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    /// <summary>Withdraws scene certification before native preparation changes poses, materials or geometry.</summary>
    internal void Begin(IReadOnlyDictionary<long, EntityRenderer> current)
    {
        if (open) throw new InvalidOperationException("Entity preparation is already active.");
        ArgumentNullException.ThrowIfNull(current);
        changes.BeginChange();
        open = true; Complete = false; failed = current.Count > capacity;
        Frame = checked(Frame + 1); ExtraPreparations = 0;
        prepared.Clear(); animated.Clear(); itemMaterials.Clear(); Renderers = []; published = null; renderers = current;
    }

    /// <summary>Preserves the native visible call while recording successful once-only preparation.</summary>
    internal void PrepareVisible(EntityRenderer renderer, float dt)
    {
        if (!open) throw new InvalidOperationException("No entity preparation interval is active.");
        if (prepared.Contains(renderer)) return;
        renderer.BeforeRender(dt);
        if (prepared.Count < capacity)
        {
            PrepareItem(renderer, dt);
            prepared.Add(renderer);
        }
        else failed = true;
    }

    /// <summary>Prepares offscreen geometry even when the entity has no animation manager.</summary>
    internal IAnimationManager? AnimationFor(Entity entity, float dt)
    {
        if (!open) throw new InvalidOperationException("No entity preparation interval is active.");
        if (!failed && renderers!.TryGetValue(entity.EntityId, out var renderer)
            && ReferenceEquals(renderer.entity, entity) && !prepared.Contains(renderer))
        {
            renderer.BeforeRender(dt);
            PrepareItem(renderer, dt);
            prepared.Add(renderer);
            ExtraPreparations++;
        }
        return entity.AnimManager;
    }

    /// <summary>Advances the native animation call exactly once, temporarily requesting current offscreen matrices.</summary>
    internal void Advance(Entity entity, IAnimationManager animation, float dt)
    {
        if (!open) throw new InvalidOperationException("No entity preparation interval is active.");
        bool visible = entity.IsRendered;
        try
        {
            // Native animation managers may skip matrix preparation for invisible entities.
            // Visibility is scoped to the existing animation call, never to camera drawing.
            if (!failed) entity.IsRendered = true;
            if (animated.Count >= capacity) { failed = true; animation.OnClientFrame(dt); }
            else if (animated.Add(animation)) animation.OnClientFrame(dt);
        }
        finally { entity.IsRendered = visible; }
    }

    /// <summary>Closes changes even after failure; unknown deformation bounds conservatively invalidate all prior depths.</summary>
    internal void End(bool nativeFailed)
    {
        if (!open) throw new InvalidOperationException("No entity preparation interval is active.");
        try
        {
            Complete = !failed && !nativeFailed && renderers!.Count == prepared.Count;
            if (Complete)
            {
                var snapshot = ImmutableArray.CreateBuilder<EntityRenderer>(prepared.Count);
                // Preserve native enumeration order without making membership a persistent scene registry.
                foreach (var renderer in renderers.Values)
                {
                    if (!prepared.Contains(renderer)) { Complete = false; break; }
                    snapshot.Add(renderer);
                }
                if (Complete) { Renderers = snapshot.MoveToImmutable(); published = renderers; }
            }
            // Native custom renderers expose no guaranteed deformed bounds. Unknown extents
            // advance the existing broad epoch before retained history can be discarded.
            changes.CompleteChange(default, null, null, Frame, Frame, Frame);
        }
        finally { open = false; renderers = null; }
    }

    /// <summary>Checks original native membership directly so removal/replacement cannot survive a prepared frame certificate.</summary>
    internal bool Matches(EntityRenderer renderer, long frame) => Complete && !open && Frame == frame &&
        published is not null && published.Count == Renderers.Length && prepared.Contains(renderer) &&
        published.TryGetValue(renderer.entity.EntityId, out var current) && ReferenceEquals(current, renderer);

    /// <summary>Returns the ground material prepared at the existing once-per-frame native boundary.</summary>
    internal ItemRenderInfo? ItemMaterial(EntityItemRenderer renderer, long frame)
    {
        if (!Matches(renderer, frame) || !itemMaterials.TryGetValue(renderer, out var info))
            throw new InvalidOperationException("Native dropped-item preparation is unavailable.");
        return info;
    }
    #endregion

    #region Private
    /// <summary>Supplements the native dropped-item owner without a second renderer traversal or animation tick.</summary>
    private void PrepareItem(EntityRenderer renderer, float dt)
    {
        if (renderer is EntityItemRenderer item)
            itemMaterials.Add(item, NativeShadowItemPose.Prepare(item, dt));
    }
    #endregion
}
