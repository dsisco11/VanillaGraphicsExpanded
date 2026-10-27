using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Inspects existing field metadata only while the stored-volume developer visualizer is active.</summary>
internal static class CloudStoredVolumeDiagnostics
{
    internal static int Mode {get;set;}
    private static long lastRead;
    private static WeakReference<CloudVolumeSolution>? source;
    private static string snapshot="No diagnostic observation yet.";
    #region Public API
    /// <summary>Reads exact scheduler/age data at most once per second, without allocating diagnostic GPU storage.</summary>
    internal static void Observe(CloudVolumeSolution solution,uint frame)
    {
        if(Mode==0||!solution.Storage.Ready||solution.UpdatePending)return;
        long now=Stopwatch.GetTimestamp();
        if(source is not null&&source.TryGetTarget(out var prior)&&ReferenceEquals(prior,solution)&&now-lastRead<Stopwatch.Frequency)return;
        var fields=solution.Storage.Published;var d=fields.Domain;
        RHICommandContext.Current.Barrier(new(ResourceAccess.ShaderStorage|ResourceAccess.BufferUpdate));
        using var header=fields.Scratch.MapScope<uint>(0,52,BufferMapAccess.Read);
        uint pendingAge=0,completedAge=0,fallback=0,directCompleted=0,diffuseCompleted=0;
        long occupiedOrigins=0,eligibleDirect=0,eligibleDiffuse=0;int unknownBlocks=0;
        var rays=solution.WorkStatistics;
        using var support=fields.Occupancy.MapScope<uint>(0,d.Blocks,BufferMapAccess.Read);
        using(var regions=fields.Regions.MapScope<uint>(0,d.Blocks*16,BufferMapAccess.Read))
            for(int b=0;b<regions.Span.Length;b+=16)
            {
                var r=regions.Span.Slice(b,16);
                uint origins=support.Span[b/16];
                if((origins&4u)==0u){unknownBlocks++;continue;}
                occupiedOrigins+=(origins>>3)&511u;
                if(((origins>>3)&511u)==0u)continue;
                uint count=(origins>>3)&511u;
                for(int s=0;s<2;s++)if((rays.ActiveSolvers&(1u<<s))!=0u&&r[5+s*2]!=r[6+s*2])eligibleDirect+=count;
                if((rays.ActiveSolvers&4u)!=0u&&(r[0]&4u)!=0u)eligibleDiffuse+=count;
                if((r[0]&2)!=0)pendingAge=Math.Max(pendingAge,frame-r[9]);
                if((r[0]&4)!=0)pendingAge=Math.Max(pendingAge,frame-r[14]);
                if(r[13]==0)fallback++;else completedAge=Math.Max(completedAge,frame-r[15]);
                if(r[10]==frame)directCompleted++;if(r[11]==frame)directCompleted++;
                if(r[15]==frame)diffuseCompleted++;
            }
        snapshot=$"Grid {d.Size} x 66 x {d.Size}; spacing {CloudVolumeDomain.HorizontalCell:G5} x {d.VerticalCell:G5} x {CloudVolumeDomain.HorizontalCell:G5}\n"+
            $"Owned working/replacement/drawable payload: {solution.Storage.StorageBytes+solution.PublicationBytes:N0} bytes; field blocks: {d.Blocks}\n"+
            $"Density selected/remaining: {fields.DensityState.Selected} / {fields.DensityState.Remaining}\n"+
            $"Direct selected/actual: {header.Span[16]} / {directCompleted}; diffuse: {header.Span[17]} / {diffuseCompleted}\n"+
            $"Lighting backlog direct/diffuse: {header.Span[22]} / {header.Span[23]}\n"+
            $"Occupied origins: {occupiedOrigins:N0}; unknown blocks: {unknownBlocks}\n"+
            $"Compact occupied blocks: {header.Span[49]}; rebuild generation/frame: {header.Span[51]}/{header.Span[50]}\n"+
            $"Pending eligible origins direct/diffuse: {eligibleDirect:N0}/{eligibleDiffuse:N0}\n"+
            $"Direct rays/allowance: {rays.DirectRays:N0}/{rays.DirectAllowance:N0}; diffuse: {rays.DiffuseRays:N0}/{rays.DiffuseAllowance:N0}\n"+
            $"Unknown selected jobs (ray charges are bounds): {rays.UnknownDirectJobs}/{rays.UnknownDiffuseJobs}\n"+
            $"Empty dispatched origins direct/diffuse: {header.Span[16]*256L-rays.DirectRays:N0}/{rays.EmptyDiffuseOrigins:N0}\n"+
            $"Oldest occupied lighting pending/completed age: {pendingAge} / {completedAge} frames; fallback blocks: {fallback}\n"+
            $"Diffuse copy bytes: {solution.Diffuse.LastCopyBytes:N0}; jobs: {solution.Diffuse.LastJobs}\n"+
            "Density/region/age/fallback overlays use stored fields only. Readback is developer-only, once per second.";
        lastRead=now;source=new(solution);
    }
    /// <summary>Rejects statistics from retired volume lifetimes and exposes expected bounded startup separately from failure.</summary>
    internal static string Describe(CloudVolumeSolution? solution,bool failed)
    {
        if(failed)return "Stored-volume contribution withdrawn; reload shaders or switch renderer to recover.";
        if(solution is null)return "Volumetric clouds are not prepared.";
        if(!solution.Storage.Ready)return "Bounded density initialization/replacement pending; initial contribution is neutral.";
        return source is not null&&source.TryGetTarget(out var prior)&&ReferenceEquals(prior,solution)?snapshot:"Awaiting developer diagnostic observation.";
    }
    #endregion
}
