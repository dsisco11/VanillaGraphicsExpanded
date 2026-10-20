using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Coordinates private stored-density, scheduling and transport owners without owning native weather.</summary>
internal sealed class CloudVolumeSolution:IDisposable
{
    private readonly CloudVolumeDensity density;
    private readonly CloudVolumeWork work;
    private readonly CloudVolumeDirect direct;
    private readonly CloudVolumeDiffuse diffuse;
    private readonly Vector4[] planes=new Vector4[6];
    private uint? frame;
    internal CloudVolumeStorage Storage {get;}
    internal CloudVolumeAdmission Admission {get;}=new();
    internal CloudVolumeDiffuse Diffuse=>diffuse;
    internal string? Failure {get;private set;}
    #region Public API
    /// <summary>Creates independently owned algorithm components and typed executable factories.</summary>
    internal CloudVolumeSolution(ICoreAPI api,bool detail)
    {
        Storage=new(()=>CloudVolumeMaintenanceProgram.Create(api),()=>CloudVolumeCopyProgram.Create(api));
        density=new(op=>CloudVolumeDensityProgram.Create(api,op,detail));work=new(op=>CloudVolumeWorkProgram.Create(api,op));
        direct=new(op=>CloudVolumeDirectProgram.Create(api,op));diffuse=new(shadows=>CloudVolumeDiffuseProgram.Create(api,shadows));
    }
    /// <summary>Prepares only selected solvers before consumer ownership transfers.</summary>
    internal void Prepare(CloudVolumeViewSettings settings)
    {
        Storage.Prepare();density.Prepare();work.Prepare();
        if(settings.SelfShadowing)for(int op=0;op<3;op++)direct.Prepare(op);
        if(settings.Diffuse)diffuse.Prepare(settings.SelfShadowing);
    }
    /// <summary>Advances one shared frame budget, keeping startup neutral and the old domain drawable during replacement.</summary>
    internal bool Update(CloudMapInput input,GpuTexture map,AtmosphereLighting light,VgeFrameUniformBuffer camera,
        CloudVolumeViewSettings settings,Func<bool> current)
    {
        if(Failure is not null||!current())return false;
        if(frame==input.Frame)return Storage.Ready;
        try
        {
            // Admission also covers pending fields; it survives replacement and keeps older source envelopes conservatively.
            Admission.Observe(light);
            bool ready=density.Update(Storage,input,map,Admission.Encoding,.9f*light.Environment,current);
            if(density.Failure is not null)throw new InvalidOperationException(density.Failure);
            frame=input.Frame;
            if(!ready)return false; // Bounded initial fill is an expected neutral contribution, not a latched draw failure.
            CloudVolumeCoordinates.Frustum(camera,CloudVolumeCoordinates.Origin(Storage.Published.Domain,input),planes);
            if(!work.Select(Storage,light,input.Frame,planes,current,settings.SelfShadowing,settings.Diffuse)||
                !direct.Update(Storage,work,current)||!diffuse.Update(Storage,work,current))
                throw new InvalidOperationException(work.Failure??direct.Failure??diffuse.Failure??"Stored cloud transport rejected.");
            return true;
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {Failure=error.Message;Storage.Withdraw();return false;}
    }
    /// <summary>Revokes lighting certificates after feature changes while preserving compatible density and source envelopes.</summary>
    internal void ReconfigureLighting()
    {
        RenderingCleanup.Run(diffuse.Reset,direct.Reset,work.Reset);frame=null;
    }
    /// <summary>Retires every candidate-owned resource while native publications remain borrowed.</summary>
    public void Dispose()=>RenderingCleanup.Run(diffuse.Dispose,direct.Dispose,work.Dispose,density.Dispose,Storage.Dispose);
    #endregion
}
