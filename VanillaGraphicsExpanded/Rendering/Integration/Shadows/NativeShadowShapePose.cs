using System;
using System.Collections.Immutable;
using HarmonyLib;
using VanillaGraphicsExpanded.Numerics;
using EntityPlayer = Vintagestory.API.Common.EntityPlayer;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Captures the qualified native shadow transform without advancing animation or renderer smoothing.</summary>
internal static class NativeShadowShapePose
{
    private static readonly AccessTools.FieldRef<EntityShapeRenderer, float> BodyYaw =
        AccessTools.FieldRefAccess<EntityShapeRenderer, float>("bodyYawLerped");
    private static readonly AccessTools.FieldRef<EntityShapeRenderer, float> Intoxication =
        AccessTools.FieldRefAccess<EntityShapeRenderer, float>("intoxIntensity");
    private static readonly AccessTools.FieldRef<EntityPlayerShapeRenderer, float> SmoothedYaw =
        AccessTools.FieldRefAccess<EntityPlayerShapeRenderer, float>("smoothedBodyYaw");
    private static readonly Func<EntityPlayerShapeRenderer, bool> IsSelf =
        AccessTools.MethodDelegate<Func<EntityPlayerShapeRenderer, bool>>(
            AccessTools.PropertyGetter(typeof(EntityPlayerShapeRenderer), "IsSelf"));

    #region Public API
    /// <summary>Uses the native third-person shadow transform with zero time advancement and restores every qualified written field.</summary>
    internal static ImmutableArray<float> Capture(EntityShapeRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (renderer.GetType() != typeof(EntityShapeRenderer) && renderer.GetType() != typeof(EntityPlayerShapeRenderer))
            throw new NotSupportedException("Custom entity transforms require their native owner's explicit shadow adapter.");
        var original = renderer.ModelMat;
        if (original is not { Length: 16 }) throw new InvalidOperationException("Native model transform is unavailable.");
        var saved = (float[])original.Clone();
        float yaw = BodyYaw(renderer), intoxication = Intoxication(renderer);
        var player = renderer as EntityPlayerShapeRenderer;
        var entityPlayer = renderer.entity as EntityPlayer;
        bool shadowPass = entityPlayer?.selfNowShadowPass ?? false;
        float smooth = player is null ? 0 : SmoothedYaw(player);
        try
        {
            // Native shadow methods skip step-pitch and water-bobbing advancement.
            // Player yaw/intoxication code still writes fields at dt=0, so preserve
            // those fields as well as the mutable model array around this read.
            if (entityPlayer is not null) entityPlayer.selfNowShadowPass = true;
            if (player is not null) player.loadModelMatrixForPlayer(player.entity, IsSelf(player), 0, true);
            else renderer.loadModelMatrix(renderer.entity, 0, true);
            foreach (float value in renderer.ModelMat)
                if (!float.IsFinite(value)) throw new InvalidOperationException("Native model transform is not finite.");
            return ImmutableArray.CreateRange(renderer.ModelMat);
        }
        finally
        {
            saved.CopyTo(original, 0); renderer.ModelMat = original;
            BodyYaw(renderer) = yaw; Intoxication(renderer) = intoxication;
            if (entityPlayer is not null) entityPlayer.selfNowShadowPass = shadowPass;
            if (player is not null) SmoothedYaw(player) = smooth;
        }
    }

    /// <summary>Uses the same camera-relative origin as native deformation functions.</summary>
    internal static Vector3d Origin(EntityShapeRenderer renderer)
    {
        var camera = renderer.capi.World.Player.Entity.CameraPos;
        return new(camera.X, camera.Y, camera.Z);
    }
    #endregion
}
