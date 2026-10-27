using System;
using OpenTK.Graphics.OpenGL4;
using Legacy=OpenTK.Graphics.OpenGL;

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
        var observation=OpenGlStateCache.BeginNativeProgramMetadata(program,true,true,location,1,supplied:unit);
        bool returned=false;
        try {GL.ProgramUniform1(program,location,unit);returned=true;}
        finally {observation.Complete(returned);}
    }
    /// <summary>Assigns an explicit uniform block through its canonical metadata publication boundary.</summary>
    internal static void UniformBlockBinding(int program,int block,int slot)
    {
        if((uint)slot>=(uint)GpuSupport.Graphics.MaxUniformBufferBindings)throw new ArgumentOutOfRangeException(nameof(slot));
        var observation=OpenGlStateCache.BeginNativeProgramMetadata(program,true,false,-1,1,block:Legacy.ProgramInterface.UniformBlock,blockIndex:block,supplied:slot);
        bool returned=false;
        try {GL.UniformBlockBinding(program,block,slot);returned=true;}
        finally {observation.Complete(returned);}
    }
    /// <summary>Assigns an explicit storage block while retaining the manager's original executable association.</summary>
    internal static void ShaderStorageBlockBinding(int program,int block,int slot)
    {
        if((uint)slot>=(uint)GpuSupport.Graphics.MaxShaderStorageBufferBindings)throw new ArgumentOutOfRangeException(nameof(slot));
        var observation=OpenGlStateCache.BeginNativeProgramMetadata(program,true,false,-1,1,block:Legacy.ProgramInterface.ShaderStorageBlock,blockIndex:block,supplied:slot);
        bool returned=false;
        try {GL.ShaderStorageBlockBinding(program,block,slot);returned=true;}
        finally {observation.Complete(returned);}
    }
    #endregion
}
