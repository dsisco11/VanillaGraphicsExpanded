using System;
using System.Collections.Immutable;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Prepares current dropped-item poses once before demand and shadow capture.</summary>
internal static class NativeShadowItemPose
{
    private static readonly AccessTools.FieldRef<EntityItemRenderer, ItemSlot> Slot =
        AccessTools.FieldRefAccess<EntityItemRenderer, ItemSlot>("inslot");
    private static readonly AccessTools.FieldRef<EntityItemRenderer, Vec3d> Position =
        AccessTools.FieldRefAccess<EntityItemRenderer, Vec3d>("lerpedPos");
    private static readonly AccessTools.FieldRef<EntityItemRenderer, float[]> Matrix =
        AccessTools.FieldRefAccess<EntityItemRenderer, float[]>("ModelMat");
    private static readonly System.Action<EntityItemRenderer, ItemRenderInfo, bool, float> Load =
        AccessTools.MethodDelegate<System.Action<EntityItemRenderer, ItemRenderInfo, bool, float>>(
            AccessTools.Method(typeof(EntityItemRenderer), "LoadModelMatrix"));

    #region Public API
    /// <summary>Prepares native ground material and advances visible and offscreen poses before the surface gate.</summary>
    internal static ItemRenderInfo? Prepare(EntityItemRenderer renderer, float dt)
    {
        if (renderer.GetType() != typeof(EntityItemRenderer) ||
            typeof(EntityItemRenderer).Assembly.ManifestModule.ModuleVersionId != new Guid("0d8db7e9-fd74-4129-bbe8-1ff82baf23fc"))
            throw new NotSupportedException("Dropped-item capture requires the qualified native renderer.");
        if (!float.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        var slot = Slot(renderer);
        if (slot?.Itemstack is null) return null;
        var info = renderer.capi.Render.GetItemStackRenderInfo(slot, EnumItemRenderTarget.Ground, dt);
        if (info?.ModelRef is null || info.Transform is null)
            throw new InvalidOperationException("Native dropped-item material preparation is incomplete.");
        // The qualified native draw hook skips this same interpolation/advancement
        // after successful preparation and still rebuilds its visible draw matrix.
        var position = Position(renderer); var target = renderer.entity.Pos;
        position.X += (target.X - position.X) * 22 * dt;
        position.Y += (target.InternalY - position.Y) * 22 * dt;
        position.Z += (target.Z - position.Z) * 22 * dt;
        Load(renderer, info, false, dt);
        return info;
    }

    /// <summary>Reads the native shadow transform without advancing interpolation, spin, collision damping or water bobbing.</summary>
    internal static ImmutableArray<float> Capture(EntityItemRenderer renderer, ItemRenderInfo info)
    {
        var original = Matrix(renderer);
        if (original is not { Length: 16 }) throw new InvalidOperationException("Native dropped-item pose is unavailable.");
        var saved = (float[])original.Clone();
        try
        {
            Load(renderer, info, true, 0);
            foreach (float value in Matrix(renderer))
                if (!float.IsFinite(value)) throw new InvalidOperationException("Native dropped-item pose is nonfinite.");
            return ImmutableArray.CreateRange(Matrix(renderer));
        }
        finally { saved.CopyTo(original, 0); Matrix(renderer) = original; }
    }

    /// <summary>Reads original native item-slot identity for creation and replacement checks.</summary>
    internal static ItemStack? CurrentStack(EntityItemRenderer renderer) => Slot(renderer)?.Itemstack;
    #endregion
}
