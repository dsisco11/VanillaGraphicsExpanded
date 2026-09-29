using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.PBR.HeldLighting;

/// <summary>Builds a current player attachment transform from public state, without visual-renderer side effects.</summary>
internal static class HeldLightPlayerTransform
{
    #region Camera policy
    /// <summary>Matches the engine's distinction between projected first-person hands and immersive body rendering.</summary>
    internal static bool UsesHandView(ICoreClientAPI api, EntityPlayer player, bool firstPerson)
    {
        bool sleeping = player.WatchedAttributes.GetTreeAttribute("tiredness")?.GetInt("isSleeping") == 1;
        return firstPerson && (!api.Settings.Bool["immersiveFpMode"] || api.Render.CameraStuck || sleeping);
    }
    #endregion

    #region Current player transform
    /// <summary>Applies engine attachment geometry conventions using current body orientation rather than private yaw smoothing.</summary>
    internal static Matrix4x4 Build(EntityPlayerShapeRenderer renderer, EntityPlayer player, bool self, bool handView)
    {
        var api = renderer.capi;
        var observer = api.World.Player.Entity;
        var seat = player.MountedOn;
        // Numerics uses row vectors: prepend each operation from the engine's column-vector chain.
        var model = Matrix4x4.Identity;
        if (!self)
        {
            var observerSeat = observer.MountedOn;
            if (seat != null && observerSeat?.MountSupplier != null &&
                ReferenceEquals(seat.MountSupplier, observerSeat.MountSupplier))
            {
                var origin = observerSeat.SeatPosition;
                var position = seat.SeatPosition;
                model = Matrix4x4.CreateTranslation((float)(position.X - origin.X), (float)(position.Y - origin.Y),
                    (float)(position.Z - origin.Z));
            }
            else
            {
                var position = seat?.SeatPosition ?? player.Pos;
                model = Matrix4x4.CreateTranslation((float)(position.X - observer.CameraPos.X),
                    (float)(position.InternalY - observer.CameraPos.Y), (float)(position.Z - observer.CameraPos.Z));
            }
        }
        if (seat?.RenderTransform is { } seatTransform)
            model = MatrixHelper.FromColumnMajorForRowVectors(seatTransform.Values) * model;

        var shape = player.Properties.Client.Shape;
        float yaw = !self && seat?.Entity != null ? seat.Entity.Pos.Yaw : player.BodyYaw;
        model = Matrix4x4.CreateRotationX(player.Pos.Roll + (shape?.rotateX ?? 0) * GameMath.DEG2RAD) * model;
        model = Matrix4x4.CreateRotationY(yaw + (90 + (shape?.rotateY ?? 0)) * GameMath.DEG2RAD) * model;
        if (!(self && player.Swimming && handView) &&
            (!(observer.Controls.Gliding || observer.MountedOn != null) || !handView))
        {
            model = Matrix4x4.CreateRotationZ(player.WalkPitch + (shape?.rotateZ ?? 0) * GameMath.DEG2RAD) * model;
            float pitch = seat?.SeatPosition.Pitch ?? 0;
            if (pitch != 0)
                model = Matrix4x4.CreateTranslation(0, 0.5f, 0) * Matrix4x4.CreateRotationZ(pitch) *
                    Matrix4x4.CreateTranslation(0, -0.5f, 0) * model;
        }
        model = Matrix4x4.CreateRotationX(renderer.nowSwivelRad) * model;
        if (handView)
        {
            float itemFollow = player.RightHandItemSlot?.Itemstack?.ItemAttributes?["heldItemPitchFollow"].AsFloat(0.75f) ?? 0.75f;
            float follow = observer.Controls.IsFlying ? 1 :
                renderer.HeldItemPitchFollowOverride ?? itemFollow * (seat?.FpHandPitchFollow ?? 1);
            model = Matrix4x4.CreateTranslation(0, api.Settings.Float["fpHandsYOffset"], 0) *
                Matrix4x4.CreateTranslation(0, -(float)player.LocalEyePos.Y, 0) *
                Matrix4x4.CreateRotationZ((player.Pos.Pitch - MathF.PI) * follow) *
                Matrix4x4.CreateTranslation(0, (float)player.LocalEyePos.Y, 0) * model;
        }
        float size = player.Properties.Client.Size;
        return Matrix4x4.CreateTranslation(-0.5f, 0, -0.5f) * Matrix4x4.CreateScale(size) * model;
    }
    #endregion
}
