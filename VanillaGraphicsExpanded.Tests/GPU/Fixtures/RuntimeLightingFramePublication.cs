using System.Numerics;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Runs real camera-owned lighting publication for a controlled source scene without native cloud/fog admission.</summary>
internal sealed class RuntimeLightingFramePublication : IRenderer
{
    private readonly ICoreClientAPI api;
    private readonly CameraExposureRenderer camera;
    private readonly CameraExposureSettings previousSettings;
    private bool disposed;

    #region Public API
    /// <summary>Captures a unit manual camera through the normal Before callback and freezes after real light-list publication.</summary>
    internal RuntimeLightingFramePublication(ICoreClientAPI api)
    {
        this.api=api;
        previousSettings=ConfigModSystem.Config.CameraExposure;
        ConfigModSystem.Config.CameraExposure=new(){Enabled=false,ManualEV100=0,Compensation=0};
        CameraExposureRenderer? candidate=null;
        try
        {
            candidate=new(api);
            api.Event.RegisterRenderer(this,EnumRenderStage.Opaque,"tests_runtime_frame_lighting");
            camera=candidate;
        }
        catch
        {
            candidate?.Dispose();
            ConfigModSystem.Config.CameraExposure=previousSettings;
            throw;
        }
    }

    /// <summary>Runs immediately after the registered light snapshot and before any physical-light consumer.</summary>
    public double RenderOrder=>-9999;
    /// <summary>Publication is independent of terrain range.</summary>
    public int RenderRange=>int.MaxValue;

    /// <summary>Authors the controlled block field before camera freeze, retaining the completed point-light summary.</summary>
    public void OnRenderFrame(float deltaTime,EnumRenderStage stage)
    {
        if(disposed||stage!=EnumRenderStage.Opaque)return;
        // These transport scenes author a 32-lux full block field. The arbitrary mock engine
        // tables otherwise fit a different full-field intensity. Preserve their point/sky
        // calibration, including the point-light payload already captured by the real owner.
        float floor=10f/255, amplitude=1-floor;
        float reference=32f*(MathF.Pow(amplitude,3)+.064f)/MathF.Pow(amplitude,4);
        var calibration=VgeFrameRenderer.CurrentLightingCalibration with
        {
            BlockReferenceLux=reference, BlockNeutralFloor=floor,
            AccessorBlockReferenceLux=reference, AccessorBlockNeutralFloor=floor
        };
        VgeFrameRenderer.Current.SetLightingCalibration(calibration);
        var atmosphere=AtmosphereModSystem.Lighting;
        double solar=Peak(atmosphere?.DirectIrradiance??Vector3.Zero);
        double environment=Peak(atmosphere?.Environment??Vector3.Zero);
        // Synthetic rooms author at most 32-lux block and 32-nit diffuse/emissive inputs.
        // No native clouds, compatibility fog, or visible disk renderer is present here.
        var sources=FrameLightingBounds.FromSources(solar,0,VgeLightsRenderer.Current.SummedCandelaMaximum,
            environment,32,32,32,atmosphere?.ReconstructionMaximumNits??0,0);
        CameraExposureRenderer.FreezeLightingStorage(sources,VgeFrameRenderer.Current.CalibrationGeneration);
    }

    /// <summary>Withdraws frame publication before the host retires its camera/light snapshots and restores authored settings.</summary>
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;
        api.Event.UnregisterRenderer(this,EnumRenderStage.Opaque);
        camera.Dispose();
        ConfigModSystem.Config.CameraExposure=previousSettings;
    }
    #endregion

    #region Private
    /// <summary>Returns finite controlled-source metadata without converting its physical units.</summary>
    private static float Peak(Vector3 value)=>Math.Max(value.X,Math.Max(value.Y,value.Z));
    #endregion
}
