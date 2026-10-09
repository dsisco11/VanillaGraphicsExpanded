using System;
using System.Numerics;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes the world camera independently of effect owners and preserves previous-frame origin alignment.</summary>
internal sealed class VgeFrameRenderer : IRenderer
{
    private static VgeFrameRenderer? active;
    private readonly ICoreClientAPI api;
    private readonly VgeFrameUniformBuffer inputs = new();
    private readonly float[] inverseProjection = new float[16];
    private readonly float[] inverseView = new float[16];
    private readonly float[] currentViewProjection = new float[16];
    private readonly FrameCameraHistory history = new();
    private readonly FrameCameraContinuity continuity = new();
    private uint frameIndex;
    private bool published, disposed;

    #region Public API
    /// <summary>Captures the camera before sky and all opaque lighting consumers.</summary>
    public double RenderOrder => -10001;
    /// <summary>Camera publication does not depend on chunk visibility range.</summary>
    public int RenderRange => int.MaxValue;
    /// <summary>Exposes the active camera owner for engine scene-route classification.</summary>
    internal static ICoreClientAPI? ActiveApi => active is { disposed: false } owner ? owner.api : null;
    /// <summary>Returns the current world snapshot, rejecting consumers that run before publication.</summary>
    internal static VgeFrameUniformBuffer Current => active is { published: true, disposed: false } owner
        ? owner.inputs : throw new InvalidOperationException("The shared world camera has not been published.");

    /// <summary>Registers frame reset and early opaque capture under an owner independent of LumOn.</summary>
    internal VgeFrameRenderer(ICoreClientAPI api)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        active = this;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_frame_reset");
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_frame_camera");
        api.Event.LeaveWorld += Reset;
    }

    /// <summary>Computes common transforms once for the world view and stages one immutable publication version.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed) return;
        if (stage == EnumRenderStage.Before)
        {
            published = false;
            unchecked { frameIndex++; }
            return;
        }
        if (stage != EnumRenderStage.Opaque || published) return;
        var render = api.Render;
        var camera = api.World.Player.Entity.CameraPos;
        if (!MatrixHelper.Invert(render.CurrentProjectionMatrix, inverseProjection) ||
            !MatrixHelper.Invert(render.CameraMatrixOriginf, inverseView))
            throw new InvalidOperationException("Shared camera transforms are singular.");

        // The shared history aligns previous clip coordinates to the current render-relative origin.
        MatrixHelper.Multiply(render.CurrentProjectionMatrix, render.CameraMatrixOriginf, currentViewProjection);
        var position = api.World.Player.Entity.Pos;
        bool cameraCut = continuity.Capture(deltaTime, camera.X, camera.Y, camera.Z,
            position.Yaw, position.Pitch, position.Dimension, (int)render.CameraType);
        if (cameraCut) history.Reset();
        history.Capture(currentViewProjection, camera.X, camera.Y, camera.Z);
        var origin = FrameWorldSpaceBridge.Compute(camera.X, camera.Y, camera.Z);

        var fog = render.FogColor;
        inputs.Capture(render.CurrentProjectionMatrix, render.CameraMatrixOriginf, inverseProjection,
            inverseView, history.PreviousViewProjection, currentViewProjection, new(render.FrameWidth, render.FrameHeight),
            (float)(api.World.ElapsedMilliseconds / 1000.0), frameIndex,
            new((float)camera.X, (float)camera.Y, (float)camera.Z), new(fog.X, fog.Y, fog.Z), render.FogDensity,
            new(render.ShaderUniforms.ZNear, render.ShaderUniforms.ZFar), origin.ChunkOffset,
            new((float)origin.BlockOffsetRemainder.X, (float)origin.BlockOffsetRemainder.Y, (float)origin.BlockOffsetRemainder.Z),
            float.IsFinite(deltaTime) ? Math.Max(0, deltaTime) : 0, cameraCut, render.FogMin);
        history.Commit();
        published = true;
    }

    /// <summary>Withdraws callbacks and publication before retiring the retained snapshot.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        api.Event.LeaveWorld -= Reset;
        Reset();
        inputs.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Prevents temporal transforms from crossing a world lifetime.</summary>
    private void Reset() { published = false; history.Reset(); continuity.Reset(); }
    #endregion
}
