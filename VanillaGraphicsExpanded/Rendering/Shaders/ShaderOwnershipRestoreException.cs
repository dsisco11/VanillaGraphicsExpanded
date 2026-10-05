using System;
namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Distinguishes a failed shader-owner handoff from a recoverable shader operation failure.</summary>
internal sealed class ShaderOwnershipRestoreException : Exception
{
    #region Public API
    /// <summary>Retains activation or cleanup failures that prevented restoring the incoming owner.</summary>
    internal ShaderOwnershipRestoreException(Exception cause) : base("Shader ownership restoration failed.", cause) { }
    #endregion
}
