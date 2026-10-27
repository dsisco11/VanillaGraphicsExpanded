using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns one native-state handoff for a contiguous volume update, retaining typed dispatch ownership.</summary>
internal sealed class CloudVolumeSubmission
{
    private HashSet<GpuComputeProgram>? admitted;
    #if DEBUG
    internal int Boundaries { get; private set; }
    internal int Dispatches { get; private set; }
    internal int Barriers { get; private set; }
    #endif

    #region Public API
    /// <summary>Captures the union before any mutation and restores external state even when a producer fails.</summary>
    internal void Batch(IEnumerable<GpuComputeProgram> programs, Action operation)
    {
        if(admitted is not null)throw new InvalidOperationException("Cloud update batches cannot nest.");
        var selected=programs.ToHashSet();
        var context=RHICommandContext.Current;
        using var boundary=context.EnterBoundary(new("Cloud.Volume.Update",true,compute:selected.Select(program=>program.Pipeline)));
        boundary.Run(()=>
        {
            admitted=selected;
#if DEBUG
            Boundaries++;
#endif
            try { Barrier();operation(); }
            finally { admitted=null; }
        });
    }

    /// <summary>Reuses the admitted boundary or provides standalone authority for independently exercised owners.</summary>
    internal void Run(GpuComputeProgram program,Action dispatch)
    {
        if(admitted is not null)
        {
            if(!admitted.Contains(program))throw new InvalidOperationException("Cloud program was not declared before batch entry.");
            dispatch();Barrier();
        }
        else
        {
            var context=RHICommandContext.Current;
            using var boundary=context.EnterBoundary(new("Cloud.Volume.Update",true,compute:[program.Pipeline]));
            boundary.Run(()=>
            {
#if DEBUG
                Boundaries++;
#endif
                Barrier();dispatch();Barrier();
            });
        }
        #if DEBUG
        Dispatches++;
        #endif
        GlDebug.ThrowIfErrors("Cloud volume update");
    }
    #endregion

    #region Private
    /// <summary>Closes each producer once for image, sampler, metadata, upload and indirect-command consumers.</summary>
    private void Barrier()
    {
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image|
            ResourceAccess.TextureFetch|ResourceAccess.ShaderStorage|
            ResourceAccess.BufferUpdate|ResourceAccess.TextureUpdate|ResourceAccess.Indirect));
        #if DEBUG
        Barriers++;
        #endif
    }
    #endregion
}
