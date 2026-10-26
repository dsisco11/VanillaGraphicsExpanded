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
    internal int Boundaries { get; private set; }
    internal int Dispatches { get; private set; }
    internal int Barriers { get; private set; }

    #region Public API
    /// <summary>Captures the union before any mutation and restores external state even when a producer fails.</summary>
    internal void Batch(IEnumerable<GpuComputeProgram> programs, Action operation)
    {
        if(admitted is not null)throw new InvalidOperationException("Cloud update batches cannot nest.");
        var selected=programs.ToHashSet();
        var resources=new EngineBoundaryResources();
        foreach(var program in selected)resources=resources.Union(Resources(program));
        if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.Volume.Update"),resources,_=>
        {
            admitted=selected;Boundaries++;
            try { Barrier();operation(); }
            finally { admitted=null; }
        }))throw new InvalidOperationException("Cloud update boundary rejected.");
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
            if(!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.Volume.Update"),Resources(program),_=>
                {Boundaries++;Barrier();dispatch();Barrier();}))
                throw new InvalidOperationException("Cloud update boundary rejected.");
        }
        Dispatches++;GlDebug.ThrowIfErrors("Cloud volume update");
    }
    #endregion

    #region Private
    /// <summary>Rejects unavailable executables before borrowing their exact prepared binding slots.</summary>
    private static EngineBoundaryResources Resources(GpuComputeProgram program)
    {
        if(!program.IsValid)throw new InvalidOperationException("Cloud update executable unavailable.");
        return EngineBoundaryResources.From(program.ProgramLayout.BinaryInterface?.PreparedBindings??
            throw new InvalidOperationException("Cloud update bindings unavailable."));
    }

    /// <summary>Closes each producer once for image, sampler, metadata, upload and indirect-command consumers.</summary>
    private void Barrier()
    {
        RHICommandContext.Current.Barrier(new(RHIAccess.Image|
            RHIAccess.TextureFetch|RHIAccess.ShaderStorage|
            RHIAccess.BufferUpdate|RHIAccess.TextureUpdate|RHIAccess.Indirect));
        Barriers++;
    }
    #endregion
}
