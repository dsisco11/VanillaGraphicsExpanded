using System;
using GL = OpenTK.Graphics.OpenGL4.GL;
using Legacy = OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves loader-specific native deletion outcomes without consuming the native error queue.</summary>
internal static partial class OpenGLRenderingDeletionCalls
{
    #region Internal API
    /// <summary>Reads only the admitted managed command's actual kind-specific retirement outcome.</summary>
    internal static bool WasDeletedManaged(GpuResourceKind kind, nint id)
    {
        if (id == 0) return true;
        int name = unchecked((int)id);
        // Programs and stages may remain physically alive after their names are marked for deletion.
        if (kind == GpuResourceKind.Program)
        {
            if (!GL.IsProgram(name)) return true;
            GL.GetProgram(name, Native.GetProgramParameterName.DeleteStatus, out int status);
            return status != 0;
        }
        if (kind == GpuResourceKind.Shader)
        {
            if (!GL.IsShader(name)) return true;
            GL.GetShader(name, Native.ShaderParameter.DeleteStatus, out int status);
            return status != 0;
        }
        return kind switch
        {
            GpuResourceKind.Framebuffer => !GL.IsFramebuffer(name),
            GpuResourceKind.VertexArray => !GL.IsVertexArray(name),
            GpuResourceKind.ProgramPipeline => !GL.IsProgramPipeline(name),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
    /// <summary>Uses the original engine loader even before managed RHI admission has loaded its own table.</summary>
    internal static bool WasDeletedExternal(GpuResourceKind kind, nint id)
    {
        if (id == 0) return true;
        int name = unchecked((int)id);
        if (kind == GpuResourceKind.Program)
        {
            if (!Legacy.GL.IsProgram(name)) return true;
            Legacy.GL.GetProgram(name, Legacy.GetProgramParameterName.DeleteStatus, out int status);
            return status != 0;
        }
        if (kind == GpuResourceKind.Shader)
        {
            if (!Legacy.GL.IsShader(name)) return true;
            Legacy.GL.GetShader(name, Legacy.ShaderParameter.DeleteStatus, out int status);
            return status != 0;
        }
        return kind switch
        {
            GpuResourceKind.Framebuffer => !Legacy.GL.IsFramebuffer(name),
            GpuResourceKind.VertexArray => !Legacy.GL.IsVertexArray(name),
            GpuResourceKind.ProgramPipeline => !Legacy.GL.IsProgramPipeline(name),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
    #endregion
}
