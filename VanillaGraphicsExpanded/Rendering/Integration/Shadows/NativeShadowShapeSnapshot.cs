using System;
using System.Collections.Immutable;
using HarmonyLib;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Freezes one prepared native shape's transform, animation and mesh publication without copying geometry.</summary>
internal sealed class NativeShadowShapeSnapshot
{
    private static readonly AccessTools.FieldRef<EntityShapeRenderer, MultiTextureMeshRef> MeshField =
        AccessTools.FieldRefAccess<EntityShapeRenderer, MultiTextureMeshRef>("meshRefOpaque");
    private static readonly AccessTools.FieldRef<EntityPlayerShapeRenderer, MultiTextureMeshRef> PlayerMeshField =
        AccessTools.FieldRefAccess<EntityPlayerShapeRenderer, MultiTextureMeshRef>("thirdPersonMeshRef");
    private static readonly AccessTools.FieldRef<EntityShapeRenderer, bool> Spectator =
        AccessTools.FieldRefAccess<EntityShapeRenderer, bool>("isSpectator");
    private readonly NativeShadowEntityPreparation preparation;
    private readonly IAnimator animator;
    private readonly long frame;
    private readonly double wind;
    private readonly int flags;
    private readonly NativeShadowWarpSnapshot warp;

    #region Public API
    internal EntityShapeRenderer Renderer { get; }
    internal MultiTextureMeshRef Mesh { get; }
    internal ImmutableArray<float> Model { get; }
    internal ImmutableArray<float> Joints { get; }
    internal Vector3d Origin { get; }
    internal int Dimension { get; }
    /// <summary>Charges copied pose/material metadata independently from borrowed native mesh storage.</summary>
    internal long SnapshotBytes => checked((Model.Length + Joints.Length) * 4L + 256);

    /// <summary>Captures only an existing fully prepared native shape publication within the supplied byte allowance.</summary>
    internal NativeShadowShapeSnapshot(EntityShapeRenderer renderer, NativeShadowEntityPreparation preparation, long availableBytes)
    {
        ArgumentNullException.ThrowIfNull(renderer); ArgumentNullException.ThrowIfNull(preparation);
        if (typeof(EntityShapeRenderer).Assembly.ManifestModule.ModuleVersionId != new Guid("0d8db7e9-fd74-4129-bbe8-1ff82baf23fc"))
            throw new NotSupportedException("Native shape capture requires the qualified entity renderer module.");
        if (!preparation.Matches(renderer, preparation.Frame) || Spectator(renderer))
            throw new InvalidOperationException("Native shape is not a current prepared opaque caster.");
        Mesh = CurrentMesh(renderer) ?? throw new InvalidOperationException("Native shape mesh publication is unavailable.");
        animator = CurrentAnimator(renderer) ?? throw new InvalidOperationException("Native shape animation is unavailable.");
        int count = animator.MaxJointId;
        if (count < 1 || count > ShadowEntityShader.JointCapacity || animator.Matrices.Length < count * 16 ||
            availableBytes < count * 64L + 320)
            throw new InvalidOperationException("Native shape palette exceeds its complete snapshot allowance.");
        Renderer = renderer; this.preparation = preparation; frame = preparation.Frame;
        Dimension = renderer.entity.Pos.Dimension;
        Model = NativeShadowShapePose.Capture(renderer); Origin = NativeShadowShapePose.Origin(renderer);
        Joints = ImmutableArray.Create(animator.Matrices, 0, count * 16);
        wind = renderer.WindWaveIntensity; flags = renderer.AddRenderFlags;
        warp = new(renderer.capi.Render.ShaderUniforms);
    }

    /// <summary>Rejects scene, pose, mesh, native preparation and material changes before submission or completion.</summary>
    internal bool IsCurrent => warp.IsCurrent && Renderer.entity.Pos.Dimension == Dimension && preparation.Matches(Renderer, frame) &&
        ReferenceEquals(CurrentMesh(Renderer), Mesh) && !Mesh.Disposed && !Spectator(Renderer) &&
        ReferenceEquals(CurrentAnimator(Renderer), animator) && animator.MaxJointId * 16 == Joints.Length &&
        animator.Matrices.AsSpan(0, Math.Min(animator.Matrices.Length, Joints.Length)).SequenceEqual(Joints.AsSpan()) &&
        Renderer.AddRenderFlags == flags && Renderer.WindWaveIntensity == wind &&
        NativeShadowShapePose.Origin(Renderer) == Origin && Model.AsSpan().SequenceEqual(NativeShadowShapePose.Capture(Renderer).AsSpan());

    /// <summary>Packs frozen native camera-relative material values followed by the final virtual-origin translation.</summary>
    internal void Apply(ShadowEntityShader shader, ShadowDrawView view)
    {
        if (!IsCurrent) throw new InvalidOperationException("Native shape changed after preparation.");
        Span<byte> data = stackalloc byte[208]; data.Clear();
        for (int i = 0; i < 16; i++) UboPacking.WriteFloat(data, i * 4, Model[i]);
        warp.Pack(data, (float)wind);
        for (int diagonal = 0; diagonal < 4; diagonal++) UboPacking.WriteFloat(data, 144 + diagonal * 20, 1);
        var translation = Origin - view.Origin;
        UboPacking.WriteFloat(data, 192, (float)translation.X);
        UboPacking.WriteFloat(data, 196, (float)translation.Y);
        UboPacking.WriteFloat(data, 200, (float)translation.Z);
        shader.CaptureMaterial(data, Joints.AsSpan(), flags);
    }
    #endregion

    #region Private
    /// <summary>Uses the full native player body for shadows, independent of first-person hand geometry.</summary>
    private static MultiTextureMeshRef? CurrentMesh(EntityShapeRenderer renderer) =>
        renderer is EntityPlayerShapeRenderer player ? PlayerMeshField(player) : MeshField(renderer);

    /// <summary>Matches native full-body shadows even when the self player's ordinary animation getter selects first-person hands.</summary>
    private static IAnimator? CurrentAnimator(EntityShapeRenderer renderer) =>
        renderer.entity is EntityPlayer player ? player.TpAnimManager?.Animator : renderer.entity.AnimManager?.Animator;
    #endregion
}
