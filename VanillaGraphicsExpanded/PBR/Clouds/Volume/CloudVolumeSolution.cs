using System;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Coordinates private stored-density, scheduling and transport owners without owning native weather.</summary>
internal sealed class CloudVolumeSolution:IDisposable
{
    private readonly CloudVolumeSubmission submission=new();
    private readonly CloudVolumeDensity density;
    private readonly CloudVolumeWork work;
    /// <summary>Exposes the most recently fenced solver accounting without exposing scheduler ownership.</summary>
    internal CloudVolumeWorkStatistics WorkStatistics=>work.Statistics;
    private readonly CloudVolumeDirect direct;
    private readonly CloudVolumeDiffuse diffuse;
    private readonly CloudVolumePublication publication;
    private readonly CloudVolumeUpdates updates;
    private uint? submittedFrame;
    private CloudVolumeIdentity? asyncIdentity;
    private uint asyncFrame;
    internal CloudVolumeStorage Storage {get;}
    internal CloudVolumeAdmission Admission {get;}=new();
    internal CloudVolumeDiffuse Diffuse=>diffuse;
    internal CloudVolumeViewFields? Drawable=>publication.Published;
    internal bool UpdatePending=>updates.Pending;
    /// <summary>Exposes submission counts independently of GPU timings for focused diagnostics.</summary>
    internal CloudVolumeSubmission Submission=>submission;
    internal long PublicationBytes=>publication.Bytes;
    internal string? Failure {get;private set;}
    #region Public API
    /// <summary>Creates independently owned algorithm components and typed executable factories.</summary>
    internal CloudVolumeSolution(ICoreAPI api,bool detail)
    {
        Storage=new(()=>CloudVolumeMaintenanceProgram.Create(api),()=>CloudVolumeCopyProgram.Create(api),submission);
        density=new((op,shared)=>CloudVolumeDensityProgram.Create(api,op,detail,shared),submission);work=new((op,large,shared)=>CloudVolumeWorkProgram.Create(api,op,large,shared),submission);
        direct=new(op=>CloudVolumeDirectProgram.Create(api,op),submission);diffuse=new(shadows=>CloudVolumeDiffuseProgram.Create(api,shadows),submission);
        publication=new((op,shared)=>CloudVolumePublishProgram.Create(api,op,shared));updates=new(Storage,density,work,direct,diffuse,publication,submission);
    }
    /// <summary>Prepares only selected solvers before consumer ownership transfers.</summary>
    internal void Prepare(CloudVolumeViewSettings settings)
    {
        Storage.Prepare();density.Prepare();work.Prepare();
        publication.Prepare();
        if(settings.SelfShadowing)for(int op=0;op<3;op++)direct.Prepare(op);
        if(settings.Diffuse)diffuse.Prepare(settings.SelfShadowing);
    }
    /// <summary>Checks completion before drawing, retaining the previous isolated publication while work is pending.</summary>
    internal void Poll(CloudMapInput input)
    {
        if(Failure is not null)return;
        try
        {
            var next=new CloudVolumeIdentity(input.Owner,input.World,input.Shader);
            if(asyncIdentity is not null&&(!asyncIdentity.Matches(next)||input.Frame<asyncFrame))
                {RenderingCleanup.Run(updates.Dispose,publication.Dispose,density.Reset,diffuse.Reset,direct.Reset,work.Reset,Storage.Reset);submittedFrame=null;}
            asyncIdentity=next;asyncFrame=input.Frame;updates.Poll();
        }
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error)){Failure=error.Message;Storage.Withdraw();}
    }
    /// <summary>Advances at most one update step after the current view has consumed its published snapshot.</summary>
    internal void Submit(CloudMapInput input,GpuTexture map,AtmosphereLighting lighting,VgeFrameUniformBuffer camera,
        CloudVolumeViewSettings settings,Func<bool> current)
    {
        if(Failure is not null||!current()||submittedFrame==input.Frame)return;
        try{submittedFrame=input.Frame;Admission.Observe(lighting);updates.Submit(input,map,lighting,camera,settings,Admission,current);}
        catch(Exception error)when(!EngineBoundaryRestoreException.IsRestorationFailure(error)){Failure=error.Message;Storage.Withdraw();}
    }
    /// <summary>Revokes lighting certificates after feature changes while preserving compatible density and source envelopes.</summary>
    internal void ReconfigureLighting()
    {
        RenderingCleanup.Run(updates.Dispose,density.Reset,diffuse.Reset,direct.Reset,work.Reset,publication.Dispose);submittedFrame=null;asyncIdentity=null;
    }
    /// <summary>Retires every volume-owned resource while native publications remain borrowed.</summary>
    public void Dispose()=>RenderingCleanup.Run(updates.Dispose,publication.Dispose,diffuse.Dispose,direct.Dispose,work.Dispose,density.Dispose,Storage.Dispose);
    #endregion
}
