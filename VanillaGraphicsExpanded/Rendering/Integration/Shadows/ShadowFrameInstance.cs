using System;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>One configuration generation's explicitly owned native source, admission gate and physical pages.</summary>
internal sealed record ShadowFrameInstance(IShadowFrameSource Source,NativeShadowFrameGate Gate,ShadowPagePool Pages,
    IDisposable? NativeResources=null):IDisposable
{
    #region Public API
    /// <summary>Releases a drained generation; terminal context teardown uses the RHI's existing deferred retirement.</summary>
    public void Dispose()
    {
        try{Gate.Dispose();}
        finally{try{Source.Dispose();}finally{try{Pages.Dispose();}finally{NativeResources?.Dispose();}}}
    }
    #endregion
}
