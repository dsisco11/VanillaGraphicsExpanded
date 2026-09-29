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
    private static readonly AccessTools.FieldRef<ClientSystem, ClientMain> Game =
        AccessTools.FieldRefAccess<ClientSystem, ClientMain>("game");
    private static readonly AccessTools.FieldRef<EntityPlayer, byte[]> BaseLight =
        AccessTools.FieldRefAccess<EntityPlayer, byte[]>("baseLightHsv");
    private static readonly AccessTools.FieldRef<ClientMain, Dictionary<long, EntityRenderer>> Renderers =
        AccessTools.FieldRefAccess<ClientMain, Dictionary<long, EntityRenderer>>("EntityRenderers");
    private static readonly Action<SystemRenderPlayerEffects, byte[], EntityPos> AddPointLight =
        AccessTools.MethodDelegate<Action<SystemRenderPlayerEffects, byte[], EntityPos>>(
            AccessTools.Method(typeof(SystemRenderPlayerEffects), "AddPointLight", [typeof(byte[]), typeof(EntityPos)]));
    private readonly List<(EntityPlayer Player, bool Right, int Index)> pending = new();
    private readonly double[] view = new double[16];

    #region Engine collection
    /// <summary>Starts a fresh collection; no attachment or light-array index survives across frames.</summary>
    internal static void Begin(SystemRenderPlayerEffects effects)
    {
        ClientMain game = Game(effects);
        HeldLightSources frame = Frames.GetValue(game, static _ => new HeldLightSources());
        frame.pending.Clear();
    }

    /// <summary>Replaces only the player entity emission call, preserving the engine's color conversion and limit.</summary>
    internal static void AddEntityLight(SystemRenderPlayerEffects effects, byte[] combined, Entity entity)
    {
        if (entity is not EntityPlayer player ||
            AccessTools.PropertyGetter(entity.GetType(), nameof(Entity.LightHsv)).DeclaringType != typeof(EntityPlayer))
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
        ClientMain game = Game(effects);
        HeldLightSources frame = Frames.GetValue(game, static _ => new HeldLightSources());
        // The getter has already applied its normal side effects. Its private base source excludes both hands.
        byte[]? innate = BaseLight(player);
        if (Emits(innate)) AddPointLight(effects, innate!, player.Pos);
        frame.AddHand(effects, game, player, right, true);
        frame.AddHand(effects, game, player, left, false);
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
    internal static void Complete(ClientSystem system, float dt)
    {
        ClientMain game = Game(system);
        if (!Frames.TryGetValue(game, out HeldLightSources? frame)) return;
        try
        {
            foreach (var entry in frame.pending)
            {
                if (!Renderers(game).TryGetValue(entry.Player.EntityId, out var renderer) ||
                    renderer is not EntityPlayerShapeRenderer shape) continue;
                Vec3d? position = HeldLightAttachment.Resolve(shape, entry.Player, entry.Right, dt);
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
