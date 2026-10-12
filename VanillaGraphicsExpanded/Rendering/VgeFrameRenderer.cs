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
    private readonly VgeFrameResetRenderer resetRenderer;
    private readonly float[] projection = new float[16];
    private readonly float[] view = new float[16];
    private readonly float[] inverseProjection = new float[16];
    private readonly float[] inverseView = new float[16];
    private readonly float[] currentViewProjection = new float[16];
    private readonly FrameCameraHistory history = new();
    private readonly FrameCameraContinuity continuity = new();
    private readonly PBR.EngineLightingCalibration lightingCalibration = new();
    private uint frameIndex;
    private bool published, disposed;

    #region Public API
    /// <summary>Captures after the native camera and ambient callbacks at Before order zero.</summary>
    public double RenderOrder => .001;
    /// <summary>Camera publication does not depend on chunk visibility range.</summary>
    public int RenderRange => int.MaxValue;
    /// <summary>Exposes the active camera owner for engine scene-route classification.</summary>
    internal static ICoreClientAPI? ActiveApi => active is { disposed: false } owner ? owner.api : null;
    /// <summary>Returns the current world snapshot, rejecting consumers that run before publication.</summary>
    internal static VgeFrameUniformBuffer Current => active is { published: true, disposed: false } owner
        ? owner.inputs : throw new InvalidOperationException("The shared world camera has not been published.");

    /// <summary>Reports the active source reference, using the nominated defaults before world publication.</summary>
    internal static PBR.LightingCalibration CurrentLightingCalibration => active is { published: true, disposed: false } owner
        ? owner.inputs.LightingCalibration : PBR.LightingCalibration.Default;

    /// <summary>Registers early withdrawal and post-camera publication under the shared frame owner.</summary>
    internal VgeFrameRenderer(ICoreClientAPI api)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        resetRenderer = new(this);
        active = this;
        api.Event.RegisterRenderer(resetRenderer, EnumRenderStage.Before, "vge_frame_reset");
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_frame_camera");
        api.Event.LeaveWorld += Reset;
    }

    /// <summary>Computes common transforms once for the world view and stages one immutable publication version.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || stage != EnumRenderStage.Before || published) return;
        var render = api.Render;
        var map = ((Vintagestory.Client.NoObf.ClientMain)api.World).WorldMap;
        inputs.SetLightingCalibration(lightingCalibration.Capture(map.BlockLightLevels, map.SunLightLevels,
            map.hueLevels, map.satLevels));
        // The current-matrix getters share engine scratch storage. Copy the projection immediately,
        // and use the dedicated world-camera view rather than any active pass's model-view stack.
        render.CurrentProjectionMatrix.AsSpan().CopyTo(projection);
        render.CameraMatrixOriginf.AsSpan().CopyTo(view);
        var camera = api.World.Player.Entity.CameraPos;
        if (!MatrixHelper.Invert(projection, inverseProjection) ||
            !MatrixHelper.Invert(view, inverseView))
            throw new InvalidOperationException("Shared camera transforms are singular.");

        // The shared history aligns previous clip coordinates to the current render-relative origin.
        MatrixHelper.Multiply(projection, view, currentViewProjection);
        var position = api.World.Player.Entity.Pos;
        // Sample continuity alongside the native camera update, before perception effects change
        // player angles for future camera updates. Later passes cannot advance this history again.
        bool cameraCut = continuity.Capture(deltaTime, camera.X, camera.Y, camera.Z,
            position.Yaw, position.Pitch, position.Dimension, (int)render.CameraType);
        if (cameraCut) history.Reset();
        history.Capture(currentViewProjection, camera.X, camera.Y, camera.Z);
        var origin = FrameWorldSpaceBridge.Compute(camera.X, camera.Y, camera.Z);

        var fog = render.FogColor;
        inputs.Capture(projection, view, inverseProjection,
            inverseView, history.PreviousViewProjection, currentViewProjection, new(render.FrameWidth, render.FrameHeight),
            (float)(api.World.ElapsedMilliseconds / 1000.0), frameIndex,
            new((float)camera.X, (float)camera.Y, (float)camera.Z), new(fog.X, fog.Y, fog.Z), render.FogDensity,
            new(render.ShaderUniforms.ZNear, render.ShaderUniforms.ZFar), origin.ChunkOffset,
            new((float)origin.BlockOffsetRemainder.X, (float)origin.BlockOffsetRemainder.Y, (float)origin.BlockOffsetRemainder.Z),
            float.IsFinite(deltaTime) ? Math.Max(0, deltaTime) : 0, cameraCut, render.FogMin);
        history.Commit();
        published = true;
    }

    /// <summary>Withdraws the previous publication before native camera preparation starts a new frame.</summary>
    internal void BeginFrame()
    {
        if (disposed) return;
        published = false;
        unchecked { frameIndex++; }
    }

    /// <summary>Withdraws callbacks and publication before retiring the retained snapshot.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        api.Event.UnregisterRenderer(resetRenderer, EnumRenderStage.Before);
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
