using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes returned sync pointers without truncation or status discovery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Publishes one original fence identity after an established creation result.</summary>
    [InterceptOpenGL("glFenceSync")]
    internal static void ObserveFenceSync(int condition, int flags, nint result)
    {
        if (result == 0 || condition != (int)SyncCondition.SyncGpuCommandsComplete || flags != 0
            || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        RHIResourceManagerSystem.CurrentManager!.ObserveSyncCreation(result);
    }
    #endregion
}
