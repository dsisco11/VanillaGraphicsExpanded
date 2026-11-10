using System;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Records identity at native admission, never by matching a retained light's position.</summary>
internal sealed class NativeShadowLightSources
{
    private static readonly ShadowSourceLifetimes Worlds = new();
    private static readonly ConditionalWeakTable<ClientMain, NativeShadowLightSources> Frames = new();
    private readonly ShadowSourceLifetimes owners = new();
    private readonly ConditionalWeakTable<Entity, NativeShadowHandLifetime> hands = new();
    private readonly ShadowSourceKey[] keys = new ShadowSourceKey[VgeLightsUniformBuffer.Capacity];
    private readonly double[] view = new double[16];
    private readonly long world;
    private bool begun;
    private (double X, double Y, double Z) origin;
    private int dimension;

    #region Public API
    /// <summary>Allocates identity storage for the engine's illumination capacity, not the shadow budget.</summary>
    private NativeShadowLightSources(ClientMain game) { world = Worlds.Get(game); }

    /// <summary>Obtains the collection state scoped to the actual client world instance.</summary>
    internal static NativeShadowLightSources For(ClientMain game) => Frames.GetValue(game, static g => new(g));

    /// <summary>Starts at the native list reset and preserves its world-to-view convention.</summary>
    internal void Begin(ClientMain game)
    {
        Array.Clear(keys);
        Array.Copy(game.CurrentModelViewMatrixd, view, 16);
        var camera = game.EntityPlayer.CameraPos;
        origin = (camera.X, camera.Y, camera.Z);
        dimension = game.EntityPlayer.Pos.Dimension;
        begun = true;
    }

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

    /// <summary>Records only an actually admitted native light slot.</summary>
    internal void Admit(ClientMain game, int before, ShadowSourceKey source)
    {
        if (begun && before >= 0 && before < keys.Length && game.shUniforms.PointLightsCount == before + 1)
            keys[before] = source;
    }

    /// <summary>Returns bounded identity and matrix inputs for centralized immutable capture.</summary>
    internal (long World, ReadOnlyMemory<ShadowSourceKey> Keys, ReadOnlyMemory<double> View,
        (double X, double Y, double Z) Origin, int Dimension, bool Complete) Capture(int count)
    {
        if ((uint)count > keys.Length) throw new ArgumentOutOfRangeException(nameof(count));
        bool complete = begun;
        begun = false;
        return (world, keys.AsMemory(0, count), view, origin, dimension, complete);
    }
    #endregion
}
