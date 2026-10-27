namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Publishes complete captured resources against a selected immutable executable generation.</summary>
internal partial interface IRHIBackend
{
    #region Public API
    /// <summary>Validates the full table before any native binding or current-epoch uniform allocation.</summary>
    void BindResources(RHIShaderRef shader, RHIResourceBindings inputs);
    #endregion
}
