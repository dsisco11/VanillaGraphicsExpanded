using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns one native program allocation and receives canonical metadata through its resource manager.</summary>
internal sealed class GpuProgramObject : GpuResource<GpuProgramMetadata>
{
    private int programId;
    private readonly (nint Handle, long Generation) context = Integration.RenderContextRegistry.Current();
    private string? debugName;
    private GpuProgramLayout bindingCache = new();

    #region Public API
    /// <inheritdoc />
    public override nint ResourceId
    {
        get => programId;
        protected set => programId = (int)value;
    }
    /// <summary>Gets native identity for backend execution and explicit engine integration.</summary>
    public int ProgramId => programId;
    /// <summary>Gets binding preparation derived from manager-published executable metadata.</summary>
    public GpuProgramLayout BindingCache => bindingCache;
    /// <summary>Gets the diagnostic label associated with this allocation.</summary>
    public string? DebugName => debugName;
    /// <summary>Reports native allocation usability independently of executable readiness.</summary>
    public new bool IsValid => base.IsValid;
    /// <summary>Creates an unpublished recipient and returns it only after manager-owned native creation succeeds.</summary>
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
        if (programId != 0) GlDebug.TryLabel(ObjectLabelIdentifier.Program, programId, debugName);
#endif
    }
    #endregion

    #region Internal API
    /// <summary>Gets the last manager-published executable facts without querying the driver.</summary>
    internal GpuProgramMetadata MetadataSnapshot => metadata;
    /// <summary>Reports stale derived preparation independently of native allocation validity.</summary>
    internal bool NeedsNativePreparation => bindingCache.NeedsNativePreparation;
    /// <summary>Shares installed preparation without adding another native metadata association.</summary>
    internal void SetPreparedLayout(GpuProgramLayout layout) => bindingCache = layout;
    /// <summary>Reconciles manager-published facts with this allocation's derived layout.</summary>
    internal void OnNativeProgramMetadataChanged(GpuMetadataChange change, bool available)
    {
        ulong revision = ResourceManager?.GetResourceRegistration(this)?.MetadataRevision ?? 0;
        bindingCache.ObserveNativeMetadata(programId, metadata, revision, change, available);
    }
    /// <summary>Takes deletion ownership of a program returned by an external linker and acquires its canonical facts.</summary>
    internal static GpuProgramObject Adopt(int programId)
    {
        if (programId <= 0) throw new ArgumentOutOfRangeException(nameof(programId));
        return new GpuProgramObject(programId, null);
    }
    #endregion

    #region Protected API
    /// <inheritdoc />
    protected override (nint Handle, long Generation) NativeProgramContext => context;
    /// <inheritdoc />
    protected override GpuResourceKind ResourceKind => GpuResourceKind.Program;
    #endregion

    #region Private
    /// <summary>Captures native ownership; existing external names require manager acquisition before use.</summary>
    private GpuProgramObject(int programId, string? debugName)
    {
        this.programId = programId;
        this.debugName = debugName;
        if (programId != 0) GpuResourceManager.AcquireWrapperMetadata(this, GpuProgramMetadataReader.Instance);
    }
    #endregion
}
