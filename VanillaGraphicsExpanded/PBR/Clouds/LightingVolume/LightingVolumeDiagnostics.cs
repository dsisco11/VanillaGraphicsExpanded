using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Reads existing scheduler counters only while the developer visualizer is active.</summary>
internal static class LightingVolumeDiagnostics
{
    internal static int Mode { get; set; }
    private static long lastRead;
    private static WeakReference<LightingVolumeSolution>? source;
    private static string snapshot="No diagnostic sample yet.";

    #region Public API
    /// <summary>Samples exact counts at most once per second; ordinary play performs no diagnostic readback.</summary>
    internal static void Observe(LightingVolumeSolution solution,uint frame)
    {
        if(Mode==0 || !solution.Storage.Ready) return;
        long now=Stopwatch.GetTimestamp();
        if(source is not null && source.TryGetTarget(out var prior) && ReferenceEquals(prior,solution) &&
            now-lastRead<Stopwatch.Frequency) return;
        var storage=solution.Storage;
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.BufferUpdateBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit);
        using var header=storage.Scratch!.MapScope<uint>(0,16,MapBufferAccessMask.MapReadBit);
        uint dirty=0,oldest=0;
        using(var regions=storage.Regions.MapScope<uint>(0,storage.Domain!.Regions*16,MapBufferAccessMask.MapReadBit))
            for(int i=0;i<regions.Span.Length;i+=16)
            {
                if(regions.Span[i]!=storage.Generation) continue;
                if((regions.Span[i+1]&7)!=0) dirty++;
                for(int c=0;c<3;c++)
                    if(regions.Span[i+8+c]!=0 || (regions.Span[i+1]&(1u<<c))!=0)
                        oldest=Math.Max(oldest,frame-regions.Span[i+2+c]);
            }
        snapshot=$"Selected direct: {header.Span[4]}/512; diffuse: {header.Span[5]}/256\n"+
            $"Pre-update backlog direct/diffuse: {header.Span[6]} / {header.Span[7]}; dirty regions now: {dirty}\n"+
            $"Oldest retained/pending channel: {oldest} frames (includes inactive channels)\n"+
            $"Pre-update queued ages direct/diffuse: {header.Span[8]} / {header.Span[9]}\n"+
            $"Owned payload: {storage.StorageBytes:N0} bytes; regions: {storage.Domain.Regions}\n"+
            "Transmission textures: 2; diffuse/mask textures: 2; descriptors: 2; buffers: 3\n"+
            $"Retained Q/D caps: {solution.Bounds.SourceCap:G5} / {solution.Bounds.Encoding.Cap:G5} nits\n"+
            "Coverage: blue = in-grid; red = outside. Validity: green = valid; red = fallback.\n"+
            "Completed compatible lighting switches immediately; no transition blend.\n"+
            "Readbacks are diagnostic only (at most once/second).";
        lastRead=now;source=new(solution);
    }
    /// <summary>Describes the actual selected lifetime rather than stale diagnostics from a retired solution.</summary>
    internal static string Describe(LightingVolumeSolution? solution,bool failed)
    {
        if(failed) return "Candidate publication withdrawn; reload or change selection to recover.";
        if(solution is null) return "Candidate not prepared.";
        return source is not null && source.TryGetTarget(out var previous) && ReferenceEquals(previous,solution)
            ? snapshot : "Awaiting certified native map and diagnostic observation.";
    }
    #endregion
}
