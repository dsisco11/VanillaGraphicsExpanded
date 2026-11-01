using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns a deliberately fresh native compute subject in its original physical context.</summary>
internal sealed class NativeComputeProgram : IDisposable
{
    private readonly (nint Handle, long Generation) origin = RenderContextRegistry.Current();
    private readonly RHIResourceManager? disposal = RHIResourceManagerSystem.CurrentManager;
    private int program;

    #region Public API
    /// <summary>Retires only the original executable; a dead context has already retired its native objects.</summary>
    public void Dispose()
    {
        if (program == 0) return;
        if (origin.Generation != 0 && !RenderContextRegistry.IsAlive(origin)) { program = 0; return; }
        RequireContext();
        GL.DeleteProgram(program);
        program = 0;
    }
    #endregion

    #region Internal API
    /// <summary>Exercises fresh native compilation, retiring every partial allocation on failure.</summary>
    internal NativeComputeProgram(string source, Action<int>? afterShaderCreated = null)
    {
        RequireContext();
        // Immediate context-local construction keeps its context interval. A callback can interrupt
        // that interval, so require the caller's existing cleanup service before allocating anything.
        if(afterShaderCreated is not null && disposal is null)
            throw new InvalidOperationException("Context-switchable construction requires an existing disposal service.");
        int shader = GL.CreateShader(ShaderType.ComputeShader);
        try
        {
            afterShaderCreated?.Invoke(shader);
            RequireContext();
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            if (compiled == 0) throw new InvalidOperationException(GL.GetShaderInfoLog(shader));
            program = GL.CreateProgram();
            GL.AttachShader(program, shader);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
            GL.DetachShader(program, shader);
        }
        catch
        {
            // Cleanup never selects another context or deletes an unrelated recycled name.
            RetirePartial(GpuResourceKind.Program,program);
            program = 0;
            throw;
        }
        finally
        {
            RetirePartial(GpuResourceKind.Shader,shader);
        }
    }

    /// <summary>Gets the native fixture executable only in the namespace which allocated it.</summary>
    internal int Id { get { RequireContext(); ObjectDisposedException.ThrowIf(program == 0, this); return program; } }

    /// <summary>Rejects foreign-context use before any native call.</summary>
    internal void RequireContext()
    {
        if (origin.Handle == 0 || !RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new InvalidOperationException("The checker requires its original physical context.");
    }
    #endregion

    #region Private
    /// <summary>Deletes partial allocations locally or defers them through the captured original lifecycle service.</summary>
    private void RetirePartial(GpuResourceKind kind,int id)
    {
        if(id==0 || !RenderContextRegistry.IsAlive(origin))return;
        if(RenderContextRegistry.IsCurrentResourceContext(origin))
        {
            if(kind==GpuResourceKind.Shader)GL.DeleteShader(id);else GL.DeleteProgram(id);
        }
        else GpuResource.RetireResource(kind,id,origin,disposal);
    }
    #endregion
}
