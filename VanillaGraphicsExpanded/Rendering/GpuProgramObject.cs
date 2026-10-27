using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL program object created by <c>glCreateProgram</c>.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// </summary>
internal sealed class GpuProgramObject : GpuResource<GpuProgramMetadata>, IDisposable
{
    private int programId;
    /// <summary>Exposes the last manager-published executable facts for an explicit ownership handoff.</summary>
    internal GpuProgramMetadata MetadataSnapshot => metadata;
    private readonly (nint Handle, long Generation) context = Integration.RenderContextRegistry.Current();
    protected override (nint Handle, long Generation) NativeProgramContext => context;
    private string? debugName;
    private GpuProgramLayout bindingCache = new();
    /// <summary>Retains the currently submitted link's original identity until its explicit completion.</summary>
    internal StateCache.PendingProgramLink? PendingLink { get; set; }

    /// <inheritdoc />
    public override nint ResourceId
    {
        get => programId;
        protected set => programId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Program;

    /// <summary>
    /// Gets the underlying OpenGL program id.
    /// </summary>
    public int ProgramId => programId;
    /// <summary>Gets binding preparation derived from manager-published executable metadata.</summary>
    public GpuProgramLayout BindingCache => bindingCache;

    /// <summary>Reports stale derived preparation independently of native allocation validity.</summary>
    internal bool NeedsNativePreparation => bindingCache.NeedsNativePreparation;

    /// <summary>Shares the owner's installed preparation without adding another metadata association.</summary>
    internal void SetPreparedLayout(GpuProgramLayout layout) => bindingCache = layout;

    /// <summary>Reconciles manager-published facts with this owner's existing derived layout.</summary>
    internal void OnNativeProgramMetadataChanged(GpuMetadataChange change, bool available)
    {
        ulong revision = ResourceManager?.GetResourceRegistration(this)?.MetadataRevision ?? 0;
        bindingCache.ObserveNativeMetadata(programId, metadata, revision, change, available);
    }

    /// <summary>
    /// Gets the debug name used for KHR_debug labeling (debug builds only).
    /// </summary>
    public string? DebugName => debugName;
    /// <summary>Reports native allocation usability independently of executable readiness.</summary>
    public new bool IsValid => base.IsValid;

    /// <summary>Adopts native identity and acquires its canonical executable snapshot.</summary>
    private GpuProgramObject(int programId, string? debugName)
    {
        this.programId = programId;
        this.debugName = debugName;
        if (programId != 0) GpuResourceManager.AcquireWrapperMetadata(this, GpuProgramMetadataReader.Instance);
    }

    /// <summary>Takes deletion ownership of a program returned by an external linker.</summary>
    internal static GpuProgramObject Adopt(int programId)
    {
        if (programId <= 0) throw new ArgumentOutOfRangeException(nameof(programId));
        return new GpuProgramObject(programId, null);
    }

    /// <summary>
    /// Creates a new OpenGL program object.
    /// </summary>
    public static GpuProgramObject Create(string? debugName = null)
    {
        var recipient = new GpuProgramObject(0, debugName);
        if (!GpuResourceManager.CreateProgram(new GpuProgramInitialization(), recipient, out _).HasValue)
            throw new InvalidOperationException("glCreateProgram returned 0.");
        recipient.SetDebugName(debugName);
        return recipient;
    }
    /// <summary>Stores the diagnostic label and applies it to the native allocation in debug builds.</summary>
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
            StateCache.Current.LinkProgram(this);
            GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int linkStatus);
            infoLog = GL.GetProgramInfoLog(programId) ?? string.Empty;
            bool ok = linkStatus != 0;
            if (ok && !bindingCache.HasPreparedInterface)
            {
                bindingCache = new GpuProgramLayout();
                if (ResourceManager?.GetResourceRegistration(this) is { MetadataAvailable: true } entry)
                    bindingCache.UseNativeMetadata(programId, metadata, entry.MetadataRevision);
                else bindingCache.UseNativeMetadata(programId, metadata, 0);
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
