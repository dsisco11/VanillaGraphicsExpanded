namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Creates shader executable bundles from selected immutable code and binding inputs.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Returns one ready owned executable or detected compile/link failure diagnostics.</summary>
    public RHICreationResult<RHIShader> CreateShader(RHIShaderDescription description) => backend.CreateShader(description);
    /// <summary>Retains an unpublished candidate until explicit completion or cancellation.</summary>
    public RHIPendingShaderCreation BeginCreateShader(RHIShaderDescription description) => backend.BeginCreateShader(description);
    /// <summary>Extracts an executable for owner-managed caching without selecting a native binary format.</summary>
    public RHIShaderBinary? ExportShaderBinary(RHIShaderRef shader, int maximumBytes) => backend.ExportShaderBinary(shader, maximumBytes);
    #endregion
}
