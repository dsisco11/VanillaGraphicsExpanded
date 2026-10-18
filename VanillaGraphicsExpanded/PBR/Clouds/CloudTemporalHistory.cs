using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Certifies history by content/lifecycle identity and records native material motion with precise integer grid offsets.</summary>
internal sealed class CloudTemporalHistory
{
    private long world,allocation,upload,shader;
    private int mapWidth;
    private readonly WeakReference<object> owner=new(null!);
    private uint frame,calibration;
    private Vector2 screen;
    private CloudPlacement placement;
    private CloudVolumeSettings settings;
    private bool underwater;
    private readonly float[] projection=new float[16],previousProjection=new float[16];
    private bool valid;

    #region Public API
    /// <summary>Withdraws history on missing input, bypass, reload, resize, mode changes or teardown.</summary>
    internal void Invalidate() { valid=false; owner.SetTarget(null!); }
    /// <summary>Reprojects only consecutive compatible main-view frames, irrespective of reusable texture names.</summary>
    internal bool Capture(CloudMapInput input,VgeFrameUniformBuffer camera,CloudVolumeSettings quality,
        CloudVolumeParameters parameters,int width,int height,CloudTemporalParameters output)
    {
        bool compatible=valid && !camera.CameraCut && camera.FrameIndex==input.Frame && unchecked(frame+1)==input.Frame
            && owner.TryGetTarget(out var previousOwner) && ReferenceEquals(previousOwner,input.Owner) && input.Storage.Width==mapWidth
            && input.World==world && input.Allocation==allocation && input.Upload>=upload && input.Shader==shader
            && camera.CalibrationGeneration==calibration && System.Runtime.InteropServices.MemoryMarshal.Read<Vector2>(camera.Bytes.Slice(384,8))==screen
            && quality==settings && parameters.UsesUnderwaterTransport==underwater
            && camera.Projection.SequenceEqual(projection);
        // Raw uploads, completed lighting publications and fog inputs evolve during normal
        // weather and day/night progression. Current-neighborhood clamping bounds that response
        // without resetting convergence at publication cadence. Only the discrete aerial/local
        // underwater transport switch changes medium identity; rolled-back uploads remain invalid.
        // Placement already contains the native view origin and residual wind. Removing the
        // integer center shift and adding committed wind reconstructs the previous material
        // point without mixing another camera origin or applying residual wind a second time.
        // Reduce integer differences in double precision before narrowing the local displacement.
        double dx=placement.Offset.X-input.Placement.Offset.X
            +((double)input.Placement.CenterX-placement.CenterX-((double)input.Placement.WindTileX-placement.WindTileX))*50;
        double dz=placement.Offset.Z-input.Placement.Offset.Z
            +((double)input.Placement.CenterZ-placement.CenterZ-((double)input.Placement.WindTileZ-placement.WindTileZ))*50;
        var translation=new Vector3((float)dx,placement.Offset.Y-input.Placement.Offset.Y,(float)dz);
        if(!float.IsFinite(translation.X)||!float.IsFinite(translation.Y)||!float.IsFinite(translation.Z)||translation.LengthSquared()>2500)
            compatible=false;
        if(!valid) CaptureCloudProjection(camera,previousProjection);
        output.Capture(previousProjection,compatible?translation:Vector3.Zero,
            width,height,input.Frame,frame,compatible,quality.TraceScale);
        return compatible;
    }
    /// <summary>Commits only after trace, reconstruction and full-resolution submission all succeed.</summary>
    internal void Commit(CloudMapInput input,VgeFrameUniformBuffer camera,CloudVolumeSettings quality,
        CloudVolumeParameters parameters)
    {
        owner.SetTarget(input.Owner); mapWidth=input.Storage.Width; world=input.World; allocation=input.Allocation; upload=input.Upload; shader=input.Shader; frame=input.Frame;
        placement=input.Placement; settings=quality; underwater=parameters.UsesUnderwaterTransport;
        camera.Projection.CopyTo(projection); CaptureCloudProjection(camera,previousProjection);
        calibration=camera.CalibrationGeneration;
        screen=System.Runtime.InteropServices.MemoryMarshal.Read<Vector2>(camera.Bytes.Slice(384,8)); valid=true;
    }
    #endregion

    #region Private
    /// <summary>Projects camera-relative cloud displacements without reapplying native view translation.</summary>
    private static void CaptureCloudProjection(VgeFrameUniformBuffer camera,Span<float> target)
    {
        // Cloud ray reconstruction applies the inverse view with w=0. Material/camera motion
        // is supplied separately by the placement delta, so native eye translation must not
        // enter this transform again. For an affine view, removing translation preserves the
        // first three VP columns and makes the fourth column equal to projection's fourth.
        camera.CurrentViewProjection.CopyTo(target);
        camera.Projection.Slice(12,4).CopyTo(target.Slice(12,4));
    }
    #endregion
}
