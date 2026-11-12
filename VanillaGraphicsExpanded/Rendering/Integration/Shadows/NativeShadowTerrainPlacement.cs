using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Freezes the native terrain origin and moving-dimension transform without touching native drawing state.</summary>
internal sealed class NativeShadowTerrainPlacement
{
    private readonly IMiniDimension? moving;
    private readonly IClientWorldAccessor world;
    private readonly Vec3i poolOrigin;
    private readonly Vector3d renderOrigin;
    private readonly Vector3d movingOffset;
    private readonly ImmutableArray<float> nativeTransform;
    private readonly float deltaTime;
    private readonly double x, y, z, yaw, pitch, roll;
    internal Vector3d Origin { get; }
    internal Vector3d Reference { get; }
    internal ImmutableArray<float> Transform { get; }
    internal bool Moving => moving is not null;

    #region Public API
    /// <summary>Captures the same offset/transform operation used by native MeshDataPoolManager.Render.</summary>
    internal NativeShadowTerrainPlacement(NativeShadowTerrainGroup group, IClientWorldAccessor world,
        Vector3d reference, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(group); ArgumentNullException.ThrowIfNull(world);
        this.world = world;
        poolOrigin = new Vec3i(checked((int)group.Origin.X), checked((int)group.Origin.Y), checked((int)group.Origin.Z));
        if (!double.IsFinite(reference.X) || !double.IsFinite(reference.Y) || !double.IsFinite(reference.Z) ||
            !float.IsFinite(deltaTime) || deltaTime < 0)
            throw new ArgumentOutOfRangeException(nameof(reference));
        Reference = reference;
        this.deltaTime = deltaTime;
        var camera = world.Player?.Entity?.CameraPos
            ?? throw new InvalidOperationException("Native terrain deformation requires the current camera origin.");
        renderOrigin = new Vector3d(camera.X, camera.Y, camera.Z);
        if (!double.IsFinite(renderOrigin.X) || !double.IsFinite(renderOrigin.Y) || !double.IsFinite(renderOrigin.Z))
            throw new InvalidOperationException("Native terrain camera origin is nonfinite.");
        float[] matrix = Mat4f.Create();
        if (group.Dimension == 1)
        {
            if (!world.TryGetMiniDimension(poolOrigin, out var dimension) || dimension.selectionTrackingOriginalPos is null)
                throw new InvalidOperationException("Moving terrain has no current native render placement.");
            moving = dimension;
            var offset = dimension.GetRenderOffset(deltaTime);
            movingOffset = new Vector3d(offset.X, offset.Y, offset.Z);
            Origin = group.Origin + movingOffset - renderOrigin;
            matrix = dimension.GetRenderTransformMatrix(matrix, new Vec3d(renderOrigin.X, renderOrigin.Y, renderOrigin.Z));
            var pose = dimension.CurrentPos;
            x = pose.X; y = pose.Y; z = pose.Z; yaw = pose.Yaw; pitch = pose.Pitch; roll = pose.Roll;
        }
        else
            Origin = group.Origin + new Vector3d(0, checked((long)group.Dimension * 32768), 0) - renderOrigin;
        if (matrix is not { Length: 16 } || !double.IsFinite(Origin.X) || !double.IsFinite(Origin.Y) || !double.IsFinite(Origin.Z))
            throw new InvalidOperationException("Native terrain transform is unavailable.");
        nativeTransform = ImmutableArray.Create(matrix);
        // Wind and perception warps consume camera-relative native positions. Rebase
        // only their final transformed output into the independent virtual view origin.
        double dx = renderOrigin.X - reference.X, dy = renderOrigin.Y - reference.Y, dz = renderOrigin.Z - reference.Z;
        for (int column = 0; column < 4; column++)
        {
            int index = column * 4;
            matrix[index] += (float)(dx * matrix[index + 3]);
            matrix[index + 1] += (float)(dy * matrix[index + 3]);
            matrix[index + 2] += (float)(dz * matrix[index + 3]);
        }
        foreach (float value in matrix)
            if (!float.IsFinite(value)) throw new InvalidOperationException("Native terrain transform is nonfinite.");
        Transform = ImmutableArray.Create(matrix);
    }

    /// <summary>Rejects a changed or removed moving placement before recording native geometry draws.</summary>
    internal bool IsCurrent => world.Player?.Entity?.CameraPos is { } camera &&
        camera.X == renderOrigin.X && camera.Y == renderOrigin.Y && camera.Z == renderOrigin.Z &&
        (moving is null || (world.TryGetMiniDimension(poolOrigin, out var current) &&
        ReferenceEquals(current, moving) && moving.selectionTrackingOriginalPos is not null &&
        moving.CurrentPos.X == x && moving.CurrentPos.Y == y && moving.CurrentPos.Z == z &&
        moving.CurrentPos.Yaw == yaw && moving.CurrentPos.Pitch == pitch && moving.CurrentPos.Roll == roll && MovingInputsCurrent()));
    #endregion

    #region Private
    /// <summary>Includes selection-following offsets and rotation pivots in the native placement certificate.</summary>
    private bool MovingInputsCurrent()
    {
        // The qualified native operations are read-only. Pose alone is insufficient:
        // selection tracking and center-of-mass edits can move geometry without it.
        var offset = moving!.GetRenderOffset(deltaTime);
        if (offset.X != movingOffset.X || offset.Y != movingOffset.Y || offset.Z != movingOffset.Z) return false;
        var current = moving.GetRenderTransformMatrix(Mat4f.Create(), new Vec3d(renderOrigin.X, renderOrigin.Y, renderOrigin.Z));
        return current is { Length: 16 } && nativeTransform.AsSpan().SequenceEqual(current);
    }
    #endregion
}
