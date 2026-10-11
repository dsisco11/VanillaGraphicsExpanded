using System;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.Rendering.Integration;
/// <summary>Connects feature withdrawal to native reload admission, retaining standalone engine event fixtures.</summary>
internal static class EngineShaderReload
{
    #region Public API
    /// <summary>Registers synchronous withdrawal when a client lifetime exists, otherwise uses the explicit engine event boundary.</summary>
    internal static IDisposable Register(ICoreClientAPI api, Vintagestory.API.Common.ActionBoolReturn withdraw)
    {
        var owner = ClientRenderingLifetime.Find(api);
        if (owner is not null) return owner.RegisterPublication(() => withdraw());
        api.Event.ReloadShader += withdraw;
        return new EventRegistration(api, withdraw);
    }
    #endregion
    #region Private
    /// <summary>Owns a standalone event subscription without creating a rendering lifetime.</summary>
    private sealed class EventRegistration(ICoreClientAPI api, Vintagestory.API.Common.ActionBoolReturn callback) : IDisposable
    {
        private bool disposed;
        /// <summary>Releases only this event registration once.</summary>
        public void Dispose() { if (disposed) return; disposed = true; api.Event.ReloadShader -= callback; }
    }
    #endregion
}
