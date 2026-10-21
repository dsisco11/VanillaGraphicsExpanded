using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Certifies history by content/lifecycle identity and records native material motion with precise integer grid offsets.</summary>
internal sealed class CloudReprojectionHistory
{
    private long world,allocation,upload,shader;
    private int mapWidth;
    private readonly WeakReference<object> owner=new(null!);
    private uint frame,calibration;
    private uint samplingEpoch,capturedEpoch,capturedFrame;
    private Vector2 screen;
    private CloudPlacement placement;
    private readonly float[] projection=new float[16];
    private bool valid;

    #region Public API
    /// <summary>Withdraws history on missing input, bypass, reload, resize, mode changes or teardown.</summary>
    internal void Invalidate() { valid=false; owner.SetTarget(null!); }
    /// <summary>Reprojects only consecutive compatible main-view frames, irrespective of reusable texture names.</summary>
    internal bool Capture(CloudMapInput input,VgeFrameUniformBuffer camera,int width,int height,CloudTemporalParameters output,
        bool contentCompatible,int divisor=2,int debugMode=0)
    {
        bool compatible=valid && !camera.CameraCut && camera.FrameIndex==input.Frame && unchecked(frame+1)==input.Frame
            && owner.TryGetTarget(out var previousOwner) && ReferenceEquals(previousOwner,input.Owner) && input.Storage.Width==mapWidth
            && input.World==world && input.Allocation==allocation && input.Upload>=upload && input.Shader==shader
            && camera.CalibrationGeneration==calibration && System.Runtime.InteropServices.MemoryMarshal.Read<Vector2>(camera.Bytes.Slice(384,8))==screen
            && contentCompatible
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
        // An exactly unchanged material-to-view transform maps pixel centres to themselves.
        // Certify it on the CPU instead of letting a floating-point matrix round trip invent
        // a nonzero neighboring footprint and inherit that neighbor's older observation age.
        bool stationary=compatible&&translation==Vector3.Zero
            &&camera.CurrentViewProjection[..12].SequenceEqual(camera.PreviousViewProjection[..12]);
        // Cold full-grid observations start at point zero. Subsequent opportunity counts
        // derive from this successful epoch, never from confidence or skipped/carry frames.
        capturedFrame=input.Frame;capturedEpoch=compatible?samplingEpoch:input.Frame;
        output.Capture(compatible?translation:Vector3.Zero,
            width,height,input.Frame,compatible,divisor,debugMode,stationary,capturedEpoch);
        return compatible;
    }
    /// <summary>Commits only after trace, reconstruction and full-resolution submission all succeed.</summary>
    internal void Commit(CloudMapInput input,VgeFrameUniformBuffer camera)
    {
        samplingEpoch=capturedFrame==input.Frame?capturedEpoch:input.Frame;
        owner.SetTarget(input.Owner); mapWidth=input.Storage.Width; world=input.World; allocation=input.Allocation; upload=input.Upload; shader=input.Shader; frame=input.Frame;
        placement=input.Placement;
        // Only consecutive successful cloud frames may consume the shared previous camera.
        // A skipped/failed cloud draw leaves this frame certificate behind and rejects reuse.
        camera.Projection.CopyTo(projection);
        calibration=camera.CalibrationGeneration;
        screen=System.Runtime.InteropServices.MemoryMarshal.Read<Vector2>(camera.Bytes.Slice(384,8)); valid=true;
    }
    #endregion

}
