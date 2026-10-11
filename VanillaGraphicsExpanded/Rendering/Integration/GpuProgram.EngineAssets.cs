using System;
using VanillaGraphicsExpanded.PBR;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
namespace VanillaGraphicsExpanded.Rendering.Shaders;
/// <summary>Preserves the engine-facing initialization facade at the integration boundary.</summary>
public abstract partial class GpuProgram
{
    #region Public API
    /// <summary>
    /// Associates a declaration with its application without reading binaries or creating an executable.
    /// </summary>
    public void Initialize(ICoreClientAPI api, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        Initialize(Integration.EngineShaderAssets.Create(api.Assets,
            string.IsNullOrWhiteSpace(AssetDomain) ? ShaderImportsSystem.DefaultDomain : AssetDomain, logger ?? api.Logger));

    }

    #endregion
}
