using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Freezes a prepared dropped item's original ground mesh, pose and native material inputs.</summary>
internal sealed class NativeShadowItemSnapshot
{
    private readonly EntityItemRenderer renderer;
    private readonly NativeShadowEntityPreparation preparation;
    private readonly long frame;
    private readonly ItemStack? stack;
    private readonly ItemRenderInfo? info;
    private readonly ImmutableArray<float> model;
    private readonly NativeShadowWarpSnapshot warp;
    private readonly Vector3d origin, target;
    private readonly int flags;
    private readonly float alphaTest, damageEffect;

    #region Public API
    internal MultiTextureMeshRef? Mesh { get; }
    internal int Dimension { get; }
    internal long SnapshotBytes => 512;

    /// <summary>Consumes the existing native preparation publication without another material callback or pose tick.</summary>
    internal NativeShadowItemSnapshot(EntityItemRenderer renderer, NativeShadowEntityPreparation preparation)
    {
        this.renderer = renderer; this.preparation = preparation; frame = preparation.Frame;
        Dimension = renderer.entity.Pos.Dimension;
        info = preparation.ItemMaterial(renderer, frame);
        stack = NativeShadowItemPose.CurrentStack(renderer);
        warp = new(renderer.capi.Render.ShaderUniforms);
        origin = Camera(); target = Target(); flags = CurrentFlags();
        if (stack is null) return;
        if (info?.ModelRef is null || info.Transform is null)
            throw new InvalidOperationException("Native dropped-item material is incomplete.");
        Mesh = info.ModelRef; alphaTest = info.AlphaTest; damageEffect = info.DamageEffect;
        model = NativeShadowItemPose.Capture(renderer, info);
    }

    /// <summary>Rejects replacement, removal, native interpolation, material and frame-uniform changes before completion.</summary>
    internal bool IsCurrent => preparation.Matches(renderer, frame) && renderer.entity.Pos.Dimension == Dimension && warp.IsCurrent &&
        ReferenceEquals(NativeShadowItemPose.CurrentStack(renderer), stack) && origin == Camera() && target == Target() && flags == CurrentFlags() &&
        (stack is null ? Mesh is null : Mesh is not null && !Mesh.Disposed && ReferenceEquals(info?.ModelRef, Mesh) &&
            info.Transform is not null && info.AlphaTest == alphaTest && info.DamageEffect == damageEffect &&
            model.AsSpan().SequenceEqual(NativeShadowItemPose.Capture(renderer, info).AsSpan()));

    /// <summary>Applies native ground-object placement before full standard-material deformation and final origin rebasing.</summary>
    internal void Apply(ShadowStaticShader shader, ShadowDrawView view)
    {
        if (Mesh is null || !IsCurrent) throw new InvalidOperationException("Native dropped-item snapshot changed.");
        Span<byte> data = stackalloc byte[ShadowMaterialParameters.BlockSize];
        ShadowMaterialParameters.PackObject(data, model.AsSpan(), warp.Deformation, origin - view.Origin);
        shader.CaptureMaterial(data); shader.CaptureFlags(flags, 0);
    }
    #endregion

    #region Private
    /// <summary>Reads the same camera-relative reference used by the native item model method.</summary>
    private Vector3d Camera()
    {
        var camera = renderer.capi.World.Player.Entity.CameraPos;
        return new(camera.X, camera.Y, camera.Z);
    }

    /// <summary>Preserves internal dimension Y in native interpolated positions.</summary>
    private Vector3d Target()
    {
        var position = renderer.entity.Pos;
        return new(position.X, position.InternalY, position.Z);
    }

    /// <summary>Matches the installed native ground renderer's swimming material flag expression exactly.</summary>
    private int CurrentFlags() => renderer.entity.Swimming && stack?.Collectible.MaterialDensity <= 1000 ? 1 << 12 : 0;
    #endregion
}
