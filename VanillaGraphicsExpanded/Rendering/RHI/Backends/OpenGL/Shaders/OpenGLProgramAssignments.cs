using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes controlled shader-interface assignments for prepared programs and injected engine integrations.</summary>
internal static class OpenGLProgramAssignments
{
    #region Internal API
    /// <summary>Preserves established integer unit assignments without changing executable selection or querying ordinary values.</summary>
    internal static void SetUnit(int program,int location,int unit)
    {
        var manager=GpuResourceManagerSystem.CurrentManager;
        manager?.ValidateProgramUnit(program,location,unit);
        if(manager?.IsProgramUnitCurrent(program,location,unit)==true)return;
        ExecuteAssignment(program,
            () => OpenGLDriverCalls.ProgramUniform1(program,location,unit), unit:true, location:location, supplied:unit);
    }
    /// <summary>Assigns an explicit uniform block through its canonical metadata publication boundary.</summary>
    internal static void UniformBlockBinding(int program,int block,int slot)
    {
        if((uint)slot>=(uint)GpuSupport.Graphics.MaxUniformBufferBindings)throw new ArgumentOutOfRangeException(nameof(slot));
        ExecuteAssignment(program,
            () => OpenGLDriverCalls.UniformBlockBinding(program,block,slot), unit:false,
            block:ProgramBlockKind.Uniform,index:block,supplied:slot);
    }
    /// <summary>Assigns an explicit storage block while retaining the manager's original executable association.</summary>
    internal static void ShaderStorageBlockBinding(int program,int block,int slot)
    {
        if((uint)slot>=(uint)GpuSupport.Graphics.MaxShaderStorageBufferBindings)throw new ArgumentOutOfRangeException(nameof(slot));
        ExecuteAssignment(program,
            () => OpenGLDriverCalls.ShaderStorageBlockBinding(program,block,slot), unit:false,
            block:ProgramBlockKind.Storage,index:block,supplied:slot);
    }
    #endregion
    #region Private
    /// <summary>Protects known program facts during a synchronous assignment and publishes supplied interface values.</summary>
    private static void ExecuteAssignment(int program, Action apply, bool unit, int location = -1,
        ProgramBlockKind block = ProgramBlockKind.Uniform, int index = -1, int supplied = 0)
    {
        var cache = OpenGlStateCache.Current;
        using var operation = cache.EnterResourceOperation(new(GpuResourceKind.Program, program));
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        var manager = GpuResourceManagerSystem.CurrentManager;
        var entry = manager?.FindObservedResource(GpuResourceKind.Program, program)
            as GpuResourceMetadataEntry<GpuProgramMetadata>;
        bool known = entry?.IsUsable == true;
        var previous = entry is null ? default : entry.Metadata;
        if (known)
        {
            bool represented = false;
            if (unit)
            {
                if (!previous.TextureUnits.IsDefault) foreach (var binding in previous.TextureUnits)
                    if (binding.Location == location) { represented = true; break; }
            }
            else foreach (var resource in previous.Resources)
                if (resource.Index == index && resource.Interface == (block == ProgramBlockKind.Uniform
                    ? ProgramResourceKind.UniformBlock : ProgramResourceKind.ShaderStorageBlock)) { represented = true; break; }
            if (!represented) entry = null;
        }
        if (entry is not null) { entry.OperationInProgress = true; entry.BeginRefresh(GpuMetadataChange.Metadata); }
        try
        {
            if (entry is not null) entry.NotifyProgramDependents(GpuMetadataChange.Metadata, available: false);
            if (operation.Cancelled) throw new InvalidOperationException("Program assignment was interrupted before execution.");
            apply();
            if (operation.Cancelled || !cache.OwnsKnowledgeFor(origin) || !Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
                throw new InvalidOperationException("Program assignment was interrupted or changed context.");
            if (entry is null) return;
            entry.OperationInProgress = false;
            if (!manager!.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live)
                throw new InvalidOperationException("Program assignment identity was retired.");
            bool success = known
                ? manager.RefreshProgramFields(entry, previous, unit, location, 1, block, index, supplied)
                : manager.RefreshMetadata(entry, GpuMetadataChange.Metadata);
            if (!success) throw new InvalidOperationException("Program assignment metadata is unavailable.");
        }
        finally { if (entry is not null) entry.OperationInProgress = false; }
    }
    #endregion
}
