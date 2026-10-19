using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Coordinates the isolated lighting owners and keeps retained-source admission paired with their lifetime.</summary>
internal sealed class LightingVolumeSolution : IDisposable
{
    private readonly LightingVolumeWork work;
    private readonly LightingVolumeDirect direct;
    private readonly LightingVolumeScattering scattering;
    private bool failed;
    internal LightingVolumeStorage Storage { get; }
    internal LightingVolumeBounds Bounds { get; }=new();
    internal string? Failure { get; private set; }

    #region Public API
    /// <summary>Creates private maintenance and solver owners with no existing cloud algorithm dependencies.</summary>
    internal LightingVolumeSolution(ICoreAPI api,bool detail)
    {
        Storage=new(()=>LightingVolumeMaintenanceProgram.Create(api));
        work=new(operation=>LightingVolumeWorkProgram.Create(api,operation));
        direct=new(()=>LightingVolumeDirectProgram.Create(api,detail));
        scattering=new(shadows=>LightingVolumeScatteringProgram.Create(api,detail,shadows));
    }
    /// <summary>Checks executables, maximum allocation support and selected transport before admission.</summary>
    internal void Prepare(LightingVolumeViewSettings settings)
    {
        if(failed) throw new InvalidOperationException(Failure);
        Storage.Prepare();work.Prepare();direct.Prepare(settings.SelfShadowing);scattering.Prepare(settings.Diffuse,settings.SelfShadowing);
    }
    /// <summary>Executes one bounded, certified observation and lighting update while retaining compatible cloud-space fields.</summary>
    internal bool Update(CloudMapInput input,GpuTexture map,AtmosphereLighting lighting,ReadOnlySpan<Vector4> planes,
        LightingVolumeViewSettings settings,Func<bool> current)
    {
        if(failed || !current()) return false;
        try
        {
            Storage.Ensure(input,Bounds.Encoding,current);
            if(!work.Update(Storage,input,map,lighting,planes,current,settings.SelfShadowing,settings.Diffuse) ||
                !direct.Update(Storage,work,input,map,current) || !scattering.Update(Storage,work,input,map,current))
                throw new InvalidOperationException(work.Failure??direct.Failure??scattering.Failure??"Lighting update certificate unavailable.");
            return true;
        }
        catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {failed=true;Failure=error.Message;Storage.Withdraw();return false;}
    }
    /// <summary>Releases all persistent fields and associated source envelopes together.</summary>
    public void Dispose()=>RenderingCleanup.Run(Storage.Dispose,work.Dispose,direct.Dispose,scattering.Dispose,Bounds.Reset);
    #endregion
}
