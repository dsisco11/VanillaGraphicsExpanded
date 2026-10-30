using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Admits client rendering systems only after checking the actual context and preparing any required restart.</summary>
public abstract class ClientRenderingModSystem : ModSystem
{
    #region Public API
    /// <summary>Blocks every startup stage before hooks or rendering resources can be installed on an unsupported context.</summary>
    public sealed override bool ShouldLoad(ICoreAPI api) =>
        api is ICoreClientAPI client && ShouldLoad(api.Side) && OpenGLStartupAdmission.Get(client);
    #endregion
}
