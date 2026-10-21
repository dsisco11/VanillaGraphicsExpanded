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
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        using var header=fields.Scratch.MapScope<uint>(0,28,MapBufferAccessMask.MapReadBit);
        uint pendingAge=0,completedAge=0,fallback=0,directCompleted=0,diffuseCompleted=0;
        using(var regions=fields.Regions.MapScope<uint>(0,d.Blocks*16,MapBufferAccessMask.MapReadBit))
            for(int b=0;b<regions.Span.Length;b+=16)
            {
                var r=regions.Span.Slice(b,16);
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
            $"Oldest diffuse pending/completed age: {pendingAge} / {completedAge} frames; fallback blocks: {fallback}\n"+
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
