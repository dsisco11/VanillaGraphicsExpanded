namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines executable-bundle creation without native stage or program handles.</summary>
internal partial interface IRHIBackend
{
    #region Public API
    /// <summary>Completes one immutable executable and its prepared binding interface synchronously.</summary>
    RHICreationResult<RHIShader> CreateShader(RHIShaderDescription description);
    /// <summary>Submits one unpublished candidate for explicit completion.</summary>
    RHIPendingShaderCreation BeginCreateShader(RHIShaderDescription description);
    /// <summary>Extracts optional cache bytes within the shader owner's explicit allocation budget.</summary>
    RHIShaderBinary? ExportShaderBinary(RHIShaderRef shader, int maximumBytes);
    #endregion
}
