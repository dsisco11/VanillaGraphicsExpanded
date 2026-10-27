namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Routes manager-owned program acquisition into the admitted native query service.</summary>
internal sealed class GpuProgramMetadataReader : IGpuResourceMetadataReader<GpuProgramMetadata>
{
    internal static readonly GpuProgramMetadataReader Instance = new();
    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => RHI.RHICommandContext.Current.OpenGL.IsProgramResource(id);
    /// <inheritdoc />
    public bool TryRead(nint id, out GpuProgramMetadata metadata)
        => RHI.RHICommandContext.Current.OpenGL.ReadProgramMetadata(id, out metadata);
    #endregion
}
