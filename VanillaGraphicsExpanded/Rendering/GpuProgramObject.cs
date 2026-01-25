using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL program object created by <c>glCreateProgram</c>.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// </summary>
internal sealed class GpuProgramObject : GpuResource, IDisposable
{
    private int programId;
    private string? debugName;
    private GpuProgramLayout bindingCache = GpuProgramLayout.Empty;

    protected override nint ResourceId
    {
        get => programId;
        set => programId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Program;

    /// <summary>
    /// Gets the underlying OpenGL program id.
    /// </summary>
    public int ProgramId => programId;

    /// <summary>
    /// Gets cached binding-related program resources (UBO/SSBO bindings, sampler/image units).
    /// Populated on successful <see cref="TryLink"/>.
    /// </summary>
    public GpuProgramLayout BindingCache => bindingCache;

    /// <summary>
    /// Gets the debug name used for KHR_debug labeling (debug builds only).
    /// </summary>
    public string? DebugName => debugName;

    /// <summary>
    /// Returns <c>true</c> when the program has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => programId != 0 && !IsDisposed;

    private GpuProgramObject(int programId, string? debugName)
    {
        this.programId = programId;
        this.debugName = debugName;
    }

    /// <summary>
    /// Creates a new OpenGL program object.
    /// </summary>
    public static GpuProgramObject Create(string? debugName = null)
    {
        int id = GL.CreateProgram();
        if (id == 0)
        {
            throw new InvalidOperationException("glCreateProgram returned 0.");
        }

#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.Program, id, debugName);
#endif

        return new GpuProgramObject(id, debugName);
    }

    /// <summary>
    /// Sets the debug label for this program (debug builds only).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
        this.debugName = debugName;

#if DEBUG
        if (programId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Program, programId, debugName);
        }
#endif
    }

    /// <summary>
    /// Attaches a compiled shader object to this program.
    /// </summary>
    public void AttachShader(int shaderId)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuProgramObject] Attempted to attach shader to disposed or invalid program");
            return;
        }

        if (shaderId == 0)
        {
            Debug.WriteLine("[GpuProgramObject] Attempted to attach invalid shader id (0)");
            return;
        }

        GL.AttachShader(programId, shaderId);
    }

    /// <summary>
    /// Detaches a shader object from this program.
    /// </summary>
    public void DetachShader(int shaderId)
    {
        if (!IsValid)
        {
            return;
        }

        if (shaderId == 0)
        {
            return;
        }

        GL.DetachShader(programId, shaderId);
    }

    /// <summary>
    /// Links this program.
    /// </summary>
    /// <param name="infoLog">Program info log from the driver.</param>
    /// <returns>True if link succeeded.</returns>
    public bool TryLink(out string infoLog)
    {
        infoLog = string.Empty;

        if (!IsValid)
        {
            return false;
        }

        try
        {
            GL.LinkProgram(programId);
            GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int linkStatus);
            infoLog = GL.GetProgramInfoLog(programId) ?? string.Empty;
            bool ok = linkStatus != 0;
            if (ok)
            {
                bindingCache = GpuProgramLayout.TryBuild(programId);
            }
            return ok;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates this program for the currently bound OpenGL state.
    /// </summary>
    /// <param name="infoLog">Program info log from the driver.</param>
    /// <returns>True if validation succeeded.</returns>
    public bool TryValidate(out string infoLog)
    {
        infoLog = string.Empty;

        if (!IsValid)
        {
            return false;
        }

        try
        {
            GL.ValidateProgram(programId);
            GL.GetProgram(programId, GetProgramParameterName.ValidateStatus, out int validateStatus);
            infoLog = GL.GetProgramInfoLog(programId) ?? string.Empty;
            return validateStatus != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the OpenGL info log for this program.
    /// </summary>
    public string GetInfoLog()
    {
        if (!IsValid)
        {
            return string.Empty;
        }

        try
        {
            return GL.GetProgramInfoLog(programId) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
