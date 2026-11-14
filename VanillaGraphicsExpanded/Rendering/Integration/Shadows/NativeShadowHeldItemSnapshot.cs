using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Captures a native third-person hand attachment and its existing item mesh without drawing or advancing animation.</summary>
internal sealed class NativeShadowHeldItemSnapshot
{
    private readonly NativeShadowShapeSnapshot shape;
    private readonly bool right, onTongs;
    private readonly ItemSlot? slot;
    private readonly ItemStack? stack;
    private readonly ItemRenderInfo? info;
    private readonly NativeShadowWarpSnapshot warp;
    private readonly ImmutableArray<float> model;
    private readonly float alphaTest, damageEffect;

    #region Public API
    internal MultiTextureMeshRef? Mesh { get; }
    /// <summary>Includes empty hands so later item creation also withdraws scene completeness.</summary>
    internal bool HasGeometry => Mesh is not null;
    internal long SnapshotBytes => 512;

    /// <summary>Resolves the same native item target, attachment and hot-item tong override used by body rendering.</summary>
    internal NativeShadowHeldItemSnapshot(NativeShadowShapeSnapshot shape, bool right)
    {
        this.shape = shape; this.right = right;
        if (!shape.IsCurrent) throw new InvalidOperationException("Native body attachment pose is unavailable.");
        warp = new(shape.Renderer.capi.Render.ShaderUniforms);
        slot = CurrentSlot(); stack = slot?.Itemstack;
        if (stack is null || slot is ItemSlotSkill) return;
        onTongs = UsesTongs();
        var pose = CurrentPose();
        if (pose?.AttachPoint is null) return;
        info = shape.Renderer.capi.Render.GetItemStackRenderInfo(slot,
            right && !onTongs ? EnumItemRenderTarget.HandTp : EnumItemRenderTarget.HandTpOff, 0);
        if (info?.ModelRef is null || info.Transform is null)
            throw new InvalidOperationException("Native held-item material preparation is incomplete.");
        Mesh = info.ModelRef;
        alphaTest = info.AlphaTest; damageEffect = info.DamageEffect;
        model = Compose(shape.Model, pose, CurrentTransform()!);
    }

    /// <summary>Rejects hand replacement, pose, mesh, material and deformation changes before publication.</summary>
    internal bool IsCurrent
    {
        get
        {
            if (!shape.IsCurrent || !warp.IsCurrent || !ReferenceEquals(CurrentSlot(), slot) ||
                !ReferenceEquals(slot?.Itemstack, stack)) return false;
            if (stack is null || slot is ItemSlotSkill) return Mesh is null;
            if (UsesTongs() != onTongs) return false;
            var pose = CurrentPose();
            if (Mesh is null) return pose?.AttachPoint is null;
            if (Mesh.Disposed || !ReferenceEquals(info?.ModelRef, Mesh) || info.AlphaTest != alphaTest ||
                info.DamageEffect != damageEffect || pose?.AttachPoint is null || CurrentTransform() is not { } transform) return false;
            return model.AsSpan().SequenceEqual(Compose(shape.Model, pose, transform).AsSpan());
        }
    }

    /// <summary>Packs native quarter-strength held-item warp after the frozen body and attachment transforms.</summary>
    internal void Apply(ShadowStaticShader shader, ShadowDrawView view)
    {
        if (!HasGeometry || !IsCurrent) throw new InvalidOperationException("Native held-item snapshot changed.");
        Span<byte> data = stackalloc byte[ShadowMaterialParameters.BlockSize];
        ShadowMaterialParameters.PackObject(data, model.AsSpan(), warp.Deformation, shape.Origin - view.Origin);
        shader.CaptureMaterial(data); shader.CaptureFlags(0, 2);
    }
    #endregion

    #region Private
    /// <summary>Reads native equipment directly, without maintaining a second entity membership table.</summary>
    private ItemSlot? CurrentSlot() => shape.Renderer.entity is EntityAgent agent
        ? right ? agent.RightHandItemSlot : agent.LeftHandItemSlot : null;

    /// <summary>Uses the native third-person animator for full-body attachments, including the self player.</summary>
    private AttachmentPointAndPose? CurrentPose()
    {
        var entity = shape.Renderer.entity;
        var animator = entity is EntityPlayer player ? player.TpAnimManager?.Animator : entity.AnimManager?.Animator;
        return animator?.GetAttachmentPointPose(right && !onTongs ? "RightHand" : "LeftHand");
    }

    /// <summary>Matches the native player-only hot-item attachment override.</summary>
    private bool UsesTongs()
    {
        if (!right || stack is null || shape.Renderer is not EntityPlayerShapeRenderer ||
            shape.Renderer.entity is not EntityAgent agent) return false;
        return stack.Collectible.GetTemperature(agent.World, stack) > GlobalConstants.TooHotToTouchTemperature &&
            agent.LeftHandItemSlot?.Itemstack?.ItemAttributes?.IsTrue("heatResistant") == true;
    }

    /// <summary>Reads the native tong transform without overwriting the cached item render-info object.</summary>
    private ModelTransform? CurrentTransform()
    {
        if (!onTongs) return info?.Transform;
        var agent = (EntityAgent)shape.Renderer.entity;
        string code = agent.LeftHandItemSlot?.Itemstack?.ItemAttributes?["transformCode"].AsString() ?? "onTongTransform";
        if (stack?.ItemAttributes?[code].Exists != true) code = "onTongTransform";
        return stack?.ItemAttributes?[code].AsObject(EntityPlayerShapeRenderer.DefaultTongTransform)
            ?? EntityPlayerShapeRenderer.DefaultTongTransform;
    }

    /// <summary>Reproduces the native RenderItem matrix chain using private values and defaulted transform copies.</summary>
    private static ImmutableArray<float> Compose(ImmutableArray<float> body, AttachmentPointAndPose pose, ModelTransform transform)
    {
        var value = transform.Clone().EnsureDefaultValues();
        var origin = value.Origin; var scale = value.ScaleXYZ;
        var translation = value.Translation; var rotation = value.Rotation; var point = pose.AttachPoint;
        var matrix = new Matrixf().Set(body.AsSpan().ToArray()).Mul(pose.AnimModelMatrix)
            .Translate(origin.X, origin.Y, origin.Z).Scale(scale.X, scale.Y, scale.Z)
            .Translate(point.PosX / 16 + translation.X, point.PosY / 16 + translation.Y, point.PosZ / 16 + translation.Z)
            .Rotate((float)(point.RotationX + rotation.X) * GameMath.DEG2RAD,
                (float)(point.RotationY + rotation.Y) * GameMath.DEG2RAD,
                (float)(point.RotationZ + rotation.Z) * GameMath.DEG2RAD)
            .Translate(-origin.X, -origin.Y, -origin.Z);
        foreach (float valueComponent in matrix.Values)
            if (!float.IsFinite(valueComponent)) throw new InvalidOperationException("Native held-item transform is nonfinite.");
        return ImmutableArray.CreateRange(matrix.Values);
    }
    #endregion
}
