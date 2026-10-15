using System;
using System.Globalization;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Postprocessing.Diagnostics;

/// <summary>Consumes a menu request at completed display publication and logs one GPU snapshot.</summary>
internal static class ExposureSnapshotLog
{
    private static bool requested;
    #region Public API
    /// <summary>Queues one snapshot without accessing GPU state from the menu callback.</summary>
    internal static void Request() => requested = true;
    /// <summary>Withdraws pending work when the owning world or display resources retire.</summary>
    internal static void Cancel() => requested = false;
    /// <summary>Performs bounded synchronous readback only on demand, before final display overwrites the scene.</summary>
    internal static void CaptureIfRequested(ICoreClientAPI api, GpuTexture scene, GpuTexture? localBase,
        LocalExposureParameters local, bool antialias)
    {
        if(!requested) return;
        requested = false;
        try
        {
            var publication = CameraExposureRenderer.DiagnosticPublication();
            using var fallback = publication.History is null ? GpuShaderStorageBuffer.Create(debugName:"ExposureSnapshot.Neutral") : null;
            fallback?.UploadData(new float[8]);
            using var output = GpuShaderStorageBuffer.Create(debugName:"ExposureSnapshot.Output");
            output.Allocate(48);
            using var program = ExposureSnapshotProgram.Create(api);
            if(!program.EnsureReady()) throw new InvalidOperationException(program.PreparationLog);
            program.Scene=scene; program.LocalBase=localBase ?? scene;
            program.History=publication.History ?? fallback!; program.Output=output;
            program.Capture(local,publication.Settings,antialias);
            var resources=EngineBoundaryResources.From(program.ProgramLayout.BinaryInterface!.PreparedBindings);
            if(!EngineBoundaryExecution.TryRun(new("Debug.ExposureSnapshot"),resources,_=>program.Dispatch(1)))
                throw new InvalidOperationException("Exposure snapshot boundary was rejected.");
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);
            var values=new float[12];
            using(output.BindScope()) GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer,IntPtr.Zero,48,values);
            api.Logger.Notification(Format(values,publication.Settings,local,publication.History is not null,
                VgeFrameRenderer.Current.FrameIndex,ConfigModSystem.Config.LumOn.Enabled,antialias));
        }
        catch(Exception error)
        {
            api.Logger.Warning("[VGE Exposure Snapshot] unavailable: {0}",error.Message);
        }
    }
    /// <summary>Formats raw values and their units invariantly so snapshots can be compared across views.</summary>
    internal static string Format(float[] data,CameraExposureParameters camera,LocalExposureParameters local,
        bool hasHistory,uint frame,bool lumOn,bool antialias)
    {
        if(data.Length!=12) throw new ArgumentException("Exposure snapshot requires twelve GPU values.",nameof(data));
        return string.Create(CultureInfo.InvariantCulture,
            $"[VGE Exposure Snapshot] frame={frame} LumOn={lumOn} auto={camera.Enabled} local={local.Enabled} FXAA={antialias} history={hasHistory} " +
            $"globalStops={data[6]:F5} minStops={camera.MinEV:F5} maxStops={camera.MaxEV:F5} compensation={camera.Compensation:F5} " +
            $"requestedStops={data[4]:F5} targetCapHit={data[5]:F0} atBrighteningLimit={data[6]>=camera.MaxEV-.0001f} " +
            $"logMiddleGray={data[1]:F5} middleGray={camera.MiddleGray:G9} meteredLog2Nits={data[2]:F5} meteringValid={data[3]:F0} " +
            $"probe=centre-pixel probeLog2Nits={data[8]:F5} baseLog2Nits={data[9]:F5} localProbeValid={data[10]:F0} " +
            $"localStops={data[7]:F5} combinedStops={data[11]:F5} combinedGain={MathF.Pow(2,data[11]):G9} " +
            $"shadowStrength={local.ShadowStrength:F3} highlightStrength={local.HighlightStrength:F3} shadowLimit={local.ShadowLimit:F3} highlightLimit={local.HighlightLimit:F3}");
    }
    #endregion
}
