using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.PBR.HeldLighting;

/// <summary>Evaluates current held-item attachments through public engine data without drawing or mutating renderer state.</summary>
internal static class HeldLightAttachment
{
    #region Attachment evaluation
    /// <summary>Resolves a hand from public current-pose and item-render APIs without invoking entity drawing.</summary>
    internal static Vec3d? Resolve(EntityPlayerShapeRenderer renderer, EntityPlayer player, bool right)
    {
        if (renderer.GetType() != typeof(EntityPlayerShapeRenderer)) return null;
        var api = renderer.capi;
        bool self = ReferenceEquals(api.World.Player.Entity, player);
        bool firstPerson = self && api.World.Player.CameraMode == EnumCameraMode.FirstPerson;
        bool handView = HeldLightPlayerTransform.UsesHandView(api, player, firstPerson);
        ItemSlot slot = right ? player.RightHandItemSlot : player.LeftHandItemSlot;
        ItemStack? stack = slot?.Itemstack;
        if (stack == null || slot is ItemSlotSkill) return null;

        // Select the normal camera's animator directly; shadow rendering must not choose the attachment pose.
        IAnimationManager? animations = firstPerson ? player.SelfFpAnimManager : player.TpAnimManager;
        animations ??= player.AnimManager;
        var leftAttributes = player.LeftHandItemSlot?.Itemstack?.ItemAttributes;
        bool onTongs = right && stack.Collectible.GetTemperature(player.World, stack) >
            Vintagestory.API.Config.GlobalConstants.TooHotToTouchTemperature &&
            leftAttributes?.IsTrue("heatResistant") == true;
        if (!onTongs && handView && api.Settings.Bool["hideFpHands"]) return null;
        var pose = animations?.Animator?.GetAttachmentPointPose(right && !onTongs ? "RightHand" : "LeftHand");
        if (pose?.AttachPoint == null) return null;
        var info = api.Render.GetItemStackRenderInfo(slot,
            right && !onTongs ? EnumItemRenderTarget.HandTp : EnumItemRenderTarget.HandTpOff, 0);
        ModelTransform? transform = info?.Transform;
        if (onTongs)
        {
            string code = leftAttributes?["transformCode"].AsString() ?? "onTongTransform";
            if (stack.ItemAttributes?[code].Exists != true) code = "onTongTransform";
            transform = stack.ItemAttributes?[code].AsObject(EntityPlayerShapeRenderer.DefaultTongTransform)
                ?? EntityPlayerShapeRenderer.DefaultTongTransform;
        }
        if (transform == null) return null;
        var top = stack.Collectible.TopMiddlePos;
        var model = HeldLightPlayerTransform.Build(renderer, player, self, handView);
        Vector4 position = Compose(model, pose, transform, new Vector4(top.X, top.Y, top.Z, 1));
        if (handView)
            position = ReprojectHand(position, api.Render.CameraMatrixOriginf,
                api.Render.CurrentProjectionMatrix, renderer.HandRenderFov);
        Vec3d origin = api.World.Player.Entity.CameraPos;
        return new Vec3d(origin.X + position.X, origin.Y + position.Y, origin.Z + position.Z);
    }
    #endregion

    #region Coordinate transforms
    /// <summary>Applies the affine transform order used by EntityShapeRenderer.RenderItem.</summary>
    internal static Vector4 Compose(Matrix4x4 model, AttachmentPointAndPose pose, ModelTransform transform, Vector4 point)
    {
        transform = transform.EnsureDefaultValues();
        var origin = transform.Origin;
        var scale = transform.ScaleXYZ;
        var translation = transform.Translation;
        var rotation = transform.Rotation;
        var attachment = pose.AttachPoint;
        // Reverse the engine's post-multiplied column-vector chain for Numerics row-vector transforms.
        var matrix = Matrix4x4.CreateTranslation(-origin.X, -origin.Y, -origin.Z) *
            Matrix4x4.CreateRotationZ((float)(attachment.RotationZ + rotation.Z) * GameMath.DEG2RAD) *
            Matrix4x4.CreateRotationY((float)(attachment.RotationY + rotation.Y) * GameMath.DEG2RAD) *
            Matrix4x4.CreateRotationX((float)(attachment.RotationX + rotation.X) * GameMath.DEG2RAD) *
            Matrix4x4.CreateTranslation((float)(attachment.PosX / 16 + translation.X),
                (float)(attachment.PosY / 16 + translation.Y), (float)(attachment.PosZ / 16 + translation.Z)) *
            Matrix4x4.CreateScale(scale.X, scale.Y, scale.Z) *
            Matrix4x4.CreateTranslation(origin.X, origin.Y, origin.Z) *
            MatrixHelper.FromColumnMajorForRowVectors(pose.AnimModelMatrix) * model;
        return Vector4.Transform(point, matrix);
    }

    /// <summary>Matches inverse(normal projection) * hand projection without changing engine GL matrices.</summary>
    internal static Vector4 ReprojectHand(Vector4 position, float[] view, float[] projection, float handFov)
    {
        var camera = MatrixHelper.FromColumnMajorForRowVectors(view);
        Vector4 point = Vector4.Transform(position, camera);
        // Both projections use the same aspect ratio and clip planes; only their X/Y scale differs.
        float ratio = 1f / (MathF.Tan(handFov * 0.5f) * projection[5]);
        point.X *= ratio;
        point.Y *= ratio;
        if (!Matrix4x4.Invert(camera, out var inverse))
            throw new InvalidOperationException("Held-light camera matrix is singular.");
        return Vector4.Transform(point, inverse);
    }
    #endregion
}
