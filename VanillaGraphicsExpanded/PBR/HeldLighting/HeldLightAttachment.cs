using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.PBR.HeldLighting;

/// <summary>Evaluates current held-item attachments without drawing or advancing the live renderer.</summary>
internal static class HeldLightAttachment
{
    private static readonly System.Func<object, object> Clone = AccessTools.MethodDelegate<System.Func<object, object>>(
        AccessTools.Method(typeof(object), "MemberwiseClone"));
    private static readonly Action<EntityShapeRenderer, float, bool, bool> RenderHeldItem =
        AccessTools.MethodDelegate<Action<EntityShapeRenderer, float, bool, bool>>(
            AccessTools.Method(typeof(EntityShapeRenderer), "RenderHeldItem"));
    private static readonly AccessTools.FieldRef<EntityPlayerShapeRenderer, RenderMode> Mode =
        AccessTools.FieldRefAccess<EntityPlayerShapeRenderer, RenderMode>("renderMode");
    [ThreadStatic] private static EntityPlayerShapeRenderer? evaluating;
    [ThreadStatic] private static Vec4f? emitter;

    #region Attachment evaluation
    /// <summary>Resolves one hand using the installed renderer's hand selection and item-render callbacks.</summary>
    internal static Vec3d? Resolve(EntityPlayerShapeRenderer renderer, EntityPlayer player, bool right, float dt)
    {
        // Only the standard renderer has an audited snapshot contract. Custom renderers use the body fallback.
        if (renderer.GetType() != typeof(EntityPlayerShapeRenderer)) return null;
        var snapshot = (EntityPlayerShapeRenderer)Clone(renderer);
        snapshot.ModelMat = Mat4f.Create();
        bool wasShadow = player.selfNowShadowPass;
        var previous = evaluating;
        var previousEmitter = emitter;
        try
        {
            player.selfNowShadowPass = false;
            bool self = ReferenceEquals(renderer.capi.World.Player.Entity, player);
            evaluating = snapshot;
            emitter = null;
            snapshot.loadModelMatrixForPlayer(player, self, dt, false);
            // This calls the engine's first/third-person and tong selection. The RenderItem hook below
            // captures its arguments and stops before any shader binding, drawing or particle spawning.
            RenderHeldItem(snapshot, 0, false, right);
            if (emitter == null) return null;
            Vec4f position = emitter;
            if (self && (int)Mode(snapshot) == 0)
            {
                // Match the engine particle path: unproject hand-FOV coordinates into the normal camera.
                var render = renderer.capi.Render;
                position = ReprojectHand(position, render.CameraMatrixOriginf,
                    render.CurrentProjectionMatrix, snapshot.HandRenderFov);
            }
            Vec3d origin = renderer.capi.World.Player.Entity.CameraPos;
            return new Vec3d(origin.X + position.X, origin.Y + position.Y, origin.Z + position.Z);
        }
        finally
        {
            player.selfNowShadowPass = wasShadow;
            evaluating = previous;
            emitter = previousEmitter;
        }
    }

    /// <summary>Prevents snapshot evaluation from writing perception offsets back into shared animation poses.</summary>
    internal static bool ApplyPerception(EntityPlayer player) => evaluating?.entity != player;

    /// <summary>Intercepts only the temporary renderer; ordinary held-item rendering continues unchanged.</summary>
    internal static bool Capture(EntityShapeRenderer renderer, ItemStack stack, AttachmentPointAndPose? pose, ItemRenderInfo? info)
    {
        if (!ReferenceEquals(renderer, evaluating)) return true;
        if (pose?.AttachPoint != null && info?.Transform != null)
        {
            var top = stack.Collectible.TopMiddlePos;
            emitter = Compose(renderer.ModelMat, pose, info.Transform, new Vec4f(top.X, top.Y, top.Z, 1));
        }
        return false;
    }
    #endregion

    #region Coordinate transforms
    /// <summary>Applies the affine transform order used by EntityShapeRenderer.RenderItem.</summary>
    internal static Vec4f Compose(float[] model, AttachmentPointAndPose pose, ModelTransform transform, Vec4f point)
    {
        transform = transform.EnsureDefaultValues();
        var origin = transform.Origin;
        var scale = transform.ScaleXYZ;
        var translation = transform.Translation;
        var rotation = transform.Rotation;
        var attachment = pose.AttachPoint;
        return new Matrixf().Set(model).Mul(pose.AnimModelMatrix)
            .Translate(origin.X, origin.Y, origin.Z)
            .Scale(scale.X, scale.Y, scale.Z)
            .Translate(attachment.PosX / 16 + translation.X, attachment.PosY / 16 + translation.Y,
                attachment.PosZ / 16 + translation.Z)
            .Rotate((float)(attachment.RotationX + rotation.X) * GameMath.DEG2RAD,
                (float)(attachment.RotationY + rotation.Y) * GameMath.DEG2RAD,
                (float)(attachment.RotationZ + rotation.Z) * GameMath.DEG2RAD)
            .Translate(-origin.X, -origin.Y, -origin.Z).TransformVector(point);
    }

    /// <summary>Matches inverse(normal projection) * hand projection without changing engine GL matrices.</summary>
    internal static Vec4f ReprojectHand(Vec4f position, float[] view, float[] projection, float handFov)
    {
        var camera = new Matrixf(view);
        Vec4f point = camera.TransformVector(position);
        // Both projections use the same aspect ratio and clip planes; only their X/Y scale differs.
        float ratio = 1f / (MathF.Tan(handFov * 0.5f) * projection[5]);
        point.X *= ratio;
        point.Y *= ratio;
        return camera.Invert().TransformVector(point);
    }
    #endregion
}
