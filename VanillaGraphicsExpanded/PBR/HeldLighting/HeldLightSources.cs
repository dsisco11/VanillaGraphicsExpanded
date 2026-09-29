using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.PBR.HeldLighting;

/// <summary>Splits held emissions during engine collection and resolves attachments after current animations.</summary>
internal sealed class HeldLightSources
{
    private static readonly ConditionalWeakTable<ClientMain, HeldLightSources> Frames = new();
    private static readonly AccessTools.FieldRef<EntityPlayer, byte[]> BaseLight =
        AccessTools.FieldRefAccess<EntityPlayer, byte[]>("baseLightHsv");
    private static readonly Dictionary<Type, bool> StandardEmission = new();
    private static readonly Action<SystemRenderPlayerEffects, byte[], EntityPos> AddPointLight =
        AccessTools.MethodDelegate<Action<SystemRenderPlayerEffects, byte[], EntityPos>>(
            AccessTools.Method(typeof(SystemRenderPlayerEffects), "AddPointLight", [typeof(byte[]), typeof(EntityPos)]));
    private readonly List<(EntityPlayer Player, bool Right, int Index)> pending = new();
    private readonly double[] view = new double[16];

    #region Engine collection
    /// <summary>Releases all pending frame work when the held-light subsystem shuts down.</summary>
    internal static void Clear() => Frames.Clear();

    /// <summary>Starts a fresh collection; no attachment or light-array index survives across frames.</summary>
    internal static void Begin(ClientMain game)
    {
        HeldLightSources frame = Frames.GetValue(game, static _ => new HeldLightSources());
        frame.pending.Clear();
    }

    /// <summary>Replaces only the player entity emission call, preserving the engine's color conversion and limit.</summary>
    internal static void AddEntityLight(SystemRenderPlayerEffects effects, byte[] combined, Entity entity, ClientMain game)
    {
        if (entity is not EntityPlayer player ||
            !UsesStandardEmission(entity.GetType()))
        {
            AddPointLight(effects, combined, entity.Pos);
            return;
        }
        byte[]? right = GetHandLight(player, player.RightHandItemSlot);
        byte[]? left = GetHandLight(player, player.LeftHandItemSlot);
        if (!Emits(right) && !Emits(left))
        {
            // Keep the original getter's spawn-glow policy and any innate/fire light when hands are dark.
            AddPointLight(effects, combined, player.Pos);
            return;
        }
        HeldLightSources frame = Frames.GetValue(game, static _ => new HeldLightSources());
        // The getter has already applied its normal side effects. Its private base source excludes both hands.
        byte[]? innate = BaseLight(player);
        if (Emits(innate)) AddPointLight(effects, innate!, player.Pos);
        frame.AddHand(effects, game, player, right, true);
        frame.AddHand(effects, game, player, left, false);
    }

    /// <summary>Checks each entity type once, preserving custom emission overrides without per-frame reflection.</summary>
    private static bool UsesStandardEmission(Type type)
    {
        if (!StandardEmission.TryGetValue(type, out bool standard))
        {
            standard = AccessTools.PropertyGetter(type, nameof(Entity.LightHsv)).DeclaringType == typeof(EntityPlayer);
            StandardEmission.Add(type, standard);
        }
        return standard;
    }

    /// <summary>Publishes a hand with a body-height fallback and records only successfully admitted entries.</summary>
    private void AddHand(SystemRenderPlayerEffects effects, ClientMain game, EntityPlayer player, byte[]? hsv, bool right)
    {
        if (!Emits(hsv)) return;
        int index = game.shUniforms.PointLightsCount;
        EntityPos position = player.Pos.Copy();
        position.Y += player.LocalEyePos.Y * 0.75;
        AddPointLight(effects, hsv!, position);
        if (game.shUniforms.PointLightsCount > index)
        {
            // Capture only when an entry was published. The getter returns reusable engine scratch storage.
            if (pending.Count == 0) Array.Copy(game.CurrentModelViewMatrixd, view, 16);
            pending.Add((player, right, index));
        }
    }

    /// <summary>Returns the item's actual dynamic emission, including extinguished-item state.</summary>
    private static byte[]? GetHandLight(EntityPlayer player, ItemSlot? slot)
    {
        ItemStack? stack = slot?.Itemstack;
        return stack?.Collectible?.GetLightHsv(player.World.BlockAccessor, null, stack);
    }

    /// <summary>Recognizes an active HSV emission.</summary>
    internal static bool Emits(byte[]? hsv) => hsv is { Length: >= 3 } && hsv[2] > 0;
    #endregion

    #region Current attachment publication
    /// <summary>Updates admitted hand positions after SystemRenderEntities has advanced this frame's animations.</summary>
    internal static void Complete(ClientMain game)
    {
        if (!Frames.TryGetValue(game, out HeldLightSources? frame)) return;
        try
        {
            foreach (var entry in frame.pending)
            {
                if (entry.Player.Properties.Client.Renderer is not EntityPlayerShapeRenderer shape) continue;
                Vec3d? position = HeldLightAttachment.Resolve(shape, entry.Player, entry.Right);
                if (position != null)
                    WriteViewPosition(game.shUniforms.PointLights3, entry.Index, frame.view, position);
            }
        }
        finally
        {
            frame.pending.Clear();
        }
    }

    /// <summary>Uses the same world-to-view convention as the engine's AddPointLight overload.</summary>
    internal static void WriteViewPosition(float[] positions, int index, double[] matrix, Vec3d world)
    {
        int offset = index * 3;
        for (int row = 0; row < 3; row++)
            positions[offset + row] = (float)(matrix[row] * world.X + matrix[4 + row] * world.Y +
                matrix[8 + row] * world.Z + matrix[12 + row]);
    }
    #endregion
}
