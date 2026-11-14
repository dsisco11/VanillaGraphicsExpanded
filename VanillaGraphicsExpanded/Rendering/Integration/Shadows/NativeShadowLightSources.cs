using System;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Records identity at native admission, never by matching a retained light's position.</summary>
internal sealed class NativeShadowLightSources
{
    private static readonly ShadowSourceLifetimes Worlds = new();
    private static readonly ConditionalWeakTable<ClientMain, NativeShadowLightSources> Frames = new();
    private readonly ShadowSourceLifetimes owners = new();
    private readonly ConditionalWeakTable<Entity, NativeShadowHandLifetime> hands = new();
    private readonly ShadowSourceKey[] keys = new ShadowSourceKey[VgeLightsUniformBuffer.Capacity];
    private readonly Vector3d?[] positions = new Vector3d?[VgeLightsUniformBuffer.Capacity];
    private readonly long world;
    private bool begun;
    private int dimension;

    #region Public API
    #region Ownership
    /// <summary>Allocates identity storage for the engine's illumination capacity, not the shadow budget.</summary>
    private NativeShadowLightSources(ClientMain game) { world = Worlds.Get(game); }

    /// <summary>Obtains the collection state scoped to the actual client world instance.</summary>
    internal static NativeShadowLightSources For(ClientMain game) => Frames.GetValue(game, static g => new(g));
    #endregion

    #region Collection boundary
    /// <summary>Starts at native list reset; source positions are captured before the engine converts them to view space.</summary>
    internal void Begin(ClientMain game)
    {
        begun=false;
        Array.Clear(keys);
        Array.Clear(positions);
        dimension = game.EntityPlayer.Pos.Dimension;
        begun = true;
    }
    #endregion

    #region Source identity
    /// <summary>Ends a world session even when the client object is reused on reconnect.</summary>
    internal static void Clear()
    {
        foreach (var entry in Frames) Worlds.Remove(entry.Key);
        Frames.Clear();
    }

    /// <summary>Creates a distinct owner lifetime and emission slot for an entity or one of its held items.</summary>
    internal ShadowSourceKey Entity(Entity entity, int slot = 0, ItemStack? item = null)
    {
        long owner = owners.Get(entity);
        long lifetime = item == null ? owner : hands.GetValue(entity, static _ => new()).Capture(slot, item, owners);
        return new(world, entity.Pos.Dimension, item == null ? ShadowSourceKind.Entity : ShadowSourceKind.Hand,
            owner, lifetime, slot);
    }

    /// <summary>Preserves registered providers and block-owned emissions without guessing identity from position.</summary>
    internal ShadowSourceKey Provider(IPointLight source, int dimension)
    {
        long lifetime = owners.Get(source);
        if (source is BlockEntity block)
            return new(world, block.Pos.dimension, ShadowSourceKind.Block, lifetime, lifetime, 0,
                block.Pos.X, block.Pos.Y, block.Pos.Z);
        return new(world, dimension, ShadowSourceKind.Provider, lifetime, lifetime, 0);
    }

    /// <summary>Ends a registration's lifetime before the same object can be registered again.</summary>
    internal void RemoveProvider(IPointLight source) => owners.Remove(source);
    #endregion

    #region Frame publication
    /// <summary>Records only an actually admitted native light slot.</summary>
    internal void Admit(ClientMain game, int before, ShadowSourceKey source, Vector3d worldPosition)
    {
        if (begun && before >= 0 && before < keys.Length && game.shUniforms.PointLightsCount == before + 1)
        {
            keys[before] = source;
            UpdatePosition(before, worldPosition);
        }
    }

    /// <summary>Returns original source coordinates alongside the matching native illumination indices.</summary>
    internal (long World, ReadOnlyMemory<ShadowSourceKey> Keys, ReadOnlyMemory<Vector3d?> Positions,
        int Dimension, bool Complete) Capture(int count)
    {
        if ((uint)count > keys.Length) throw new ArgumentOutOfRangeException(nameof(count));
        bool complete = begun;
        begun = false;
        return (world, keys.AsMemory(0, count), positions.AsMemory(0, count), dimension, complete);
    }

    /// <summary>Updates an admitted attachment before snapshot capture; invalid coordinates affect shadows only.</summary>
    internal void UpdatePosition(int index, Vector3d worldPosition)
    {
        if (!begun || (uint)index >= positions.Length || !keys[index].IsKnown) return;
        positions[index] = double.IsFinite(worldPosition.X) && double.IsFinite(worldPosition.Y) && double.IsFinite(worldPosition.Z)
            ? worldPosition : null;
    }
    #endregion
    #endregion
}
