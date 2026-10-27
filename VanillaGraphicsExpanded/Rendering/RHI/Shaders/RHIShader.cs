using System;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Owns one ready executable and its immutable prepared interface, never following asset replacement.</summary>
public sealed class RHIShader : IDisposable
{
    private readonly GpuProgramObject program;
    private readonly GpuResourceRegistration? registration;
    private readonly ulong revision;
    private readonly GpuProgramMetadata snapshot;

    #region Public API
    /// <summary>Gets the immutable selected code and binding description.</summary>
    public RHIShaderDescription Description { get; }
    /// <summary>Reports explicit owner retirement.</summary>
    public bool IsDisposed { get; private set; }
    /// <summary>Retires only this generation's executable through its original resource manager.</summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        program.Dispose();
        IsDisposed = true;
    }
    /// <summary>Creates a consumer-only reference retaining this exact executable generation.</summary>
    public static implicit operator RHIShaderRef(RHIShader? shader) => new(shader);
    #endregion

    #region Internal API
    /// <summary>Gets the prepared interface that belongs only to this ready executable.</summary>
    internal GpuProgramInterface Interface { get; }
    /// <summary>Gets the original native owner for backend execution, never for consumer mutation.</summary>
    internal GpuProgramObject Program => program;
    /// <summary>Captures a completed manager association and prepared interface without reflecting again.</summary>
    internal RHIShader(RHIShaderDescription description, GpuProgramObject program, GpuProgramInterface prepared)
    {
        Description = description; this.program = program; Interface = prepared;
        snapshot = GpuResourceManager.RequireProgramSnapshot(program);
        registration = program.ResourceManager?.GetResourceRegistration(program);
        revision = registration?.MetadataRevision ?? 0;
    }
    /// <summary>Rejects unavailable, externally changed or retired generations without native discovery.</summary>
    internal void RequireUsable()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        GpuResourceManager.RequireFixedExecutable(program, registration, revision, snapshot);
    }
    #endregion
}
