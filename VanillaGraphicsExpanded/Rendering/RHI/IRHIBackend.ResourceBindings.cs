using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Publishes complete captured resources against a selected immutable executable generation.</summary>
internal partial interface IRHIBackend
{
    #region Public API
    /// <summary>Validates the full table before any native binding or current-epoch uniform allocation.</summary>
    void BindResources(RHIShaderRef shader, RHIResourceBindings inputs);
    /// <summary>Publishes a retained logical uniform version into an explicitly selected slot.</summary>
    bool BindUniformSnapshot(int slot, RHIUniformSnapshot snapshot);
    /// <summary>Validates all declared texture slots before publishing any resource selection.</summary>
    void BindSampledTextures(ReadOnlySpan<RHISampledTextureSlot> inputs);
    #endregion
}
